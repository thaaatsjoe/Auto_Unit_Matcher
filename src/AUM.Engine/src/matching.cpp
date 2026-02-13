// FAISS Matching Engine Implementation
// Uses histogram-based similarity search via Bag-of-Words codebook

#include "matching.h"
#include <faiss/index_io.h>
#include <stdexcept>
#include <cmath>
#include <algorithm>

namespace aum {

MatchingIndex::MatchingIndex(const Codebook* codebook) 
    : codebook_(codebook) {
    if (!codebook_ || !codebook_->isReady()) {
        throw std::runtime_error("Codebook must be trained/loaded before creating index");
    }
    // Create L2 (Euclidean distance) flat index with histogram dimension
    auto baseIndex = new faiss::IndexFlatL2(codebook_->K());
    index_ = std::make_unique<faiss::IndexIDMap>(baseIndex);
}

MatchingIndex::~MatchingIndex() = default;

MatchingIndex::MatchingIndex(MatchingIndex&& other) noexcept 
    : codebook_(other.codebook_), index_(std::move(other.index_)) {
    other.codebook_ = nullptr;
}

MatchingIndex& MatchingIndex::operator=(MatchingIndex&& other) noexcept {
    if (this != &other) {
        codebook_ = other.codebook_;
        index_ = std::move(other.index_);
        other.codebook_ = nullptr;
    }
    return *this;
}

void MatchingIndex::add(const Descriptor& desc, int64_t id) {
    if (!index_) {
        throw std::runtime_error("Index not initialized");
    }
    if (!codebook_ || !codebook_->isReady()) {
        throw std::runtime_error("Codebook not available");
    }
    
    // Convert descriptor to histogram via codebook
    std::vector<float> vec = codebook_->computeHistogram(desc);
    if (vec.size() != static_cast<size_t>(codebook_->K())) {
        throw std::runtime_error("Histogram dimension mismatch");
    }
    
    // FAISS add_with_ids expects array pointers
    index_->add_with_ids(1, vec.data(), &id);
}

std::vector<MatchResult> MatchingIndex::query(const Descriptor& query, int k) {
    if (!index_) {
        throw std::runtime_error("Index not initialized");
    }
    if (!codebook_ || !codebook_->isReady()) {
        throw std::runtime_error("Codebook not available");
    }
    
    if (index_->ntotal == 0) {
        return {};  // Empty index, no results
    }
    
    // Limit k to number of items in index
    int actualK = std::min(k, static_cast<int>(index_->ntotal));
    
    // Convert query descriptor to histogram via codebook
    std::vector<float> queryVec = codebook_->computeHistogram(query);
    if (queryVec.size() != static_cast<size_t>(codebook_->K())) {
        throw std::runtime_error("Query histogram dimension mismatch");
    }
    
    // Allocate result arrays
    std::vector<float> distances(actualK);
    std::vector<int64_t> ids(actualK);
    
    // Perform search
    index_->search(1, queryVec.data(), actualK, distances.data(), ids.data());
    
    // Convert to MatchResult
    std::vector<MatchResult> results;
    results.reserve(actualK);
    
    for (int i = 0; i < actualK; ++i) {
        if (ids[i] >= 0) {  // FAISS returns -1 for invalid results
            MatchResult result;
            result.id = ids[i];
            result.distance = distances[i];
            result.confidence = distanceToConfidence(distances[i]);
            results.push_back(result);
        }
    }
    
    return results;
}

size_t MatchingIndex::size() const {
    return index_ ? static_cast<size_t>(index_->ntotal) : 0;
}

void MatchingIndex::clear() {
    if (!codebook_ || !codebook_->isReady()) {
        throw std::runtime_error("Codebook not available for clear/recreate");
    }
    // Recreate the index to clear it
    auto baseIndex = new faiss::IndexFlatL2(codebook_->K());
    index_ = std::make_unique<faiss::IndexIDMap>(baseIndex);
}

void MatchingIndex::save(const std::string& path) const {
    if (!index_) {
        throw std::runtime_error("Cannot save: index not initialized");
    }
    faiss::write_index(index_.get(), path.c_str());
}

MatchingIndex MatchingIndex::load(const std::string& path, const Codebook* codebook) {
    MatchingIndex result(codebook);
    faiss::Index* loadedIndex = faiss::read_index(path.c_str());
    result.index_.reset(dynamic_cast<faiss::IndexIDMap*>(loadedIndex));
    if (!result.index_) {
        delete loadedIndex;
        throw std::runtime_error("Loaded index is not an IndexIDMap");
    }
    return result;
}

float MatchingIndex::distanceToConfidence(float distance) const {
    // Convert L2 distance to confidence percentage
    // For L2-normalized histograms:
    //   distance 0 → identical → 100%
    //   distance ~0.5 → moderate difference → ~60%
    //   distance ~1.5 → very different → ~22%
    
    const float scale = 1.0f;
    float confidence = 100.0f * std::exp(-distance * scale);
    
    // Clamp to [0, 100]
    return std::max(0.0f, std::min(100.0f, confidence));
}

float MatchingIndex::compareDescriptors(const Descriptor& query, const Descriptor& candidate) {
    auto queryFeatures = query.getFeatures();
    auto candidateFeatures = candidate.getFeatures();
    
    if (!queryFeatures || queryFeatures->empty() || 
        !candidateFeatures || candidateFeatures->empty()) {
        return 0.0f;
    }
    
    const size_t querySize = queryFeatures->size();
    const size_t candidateSize = candidateFeatures->size();
    
    // Distance threshold for considering a point "matched"
    // FPFH histograms have 33 bins, values typically 0-30
    // A threshold of 100.0 on squared L2 means ~3 units average difference per bin
    const float matchThreshold = 100.0f;
    
    int matchedPoints = 0;
    float totalMinDistance = 0.0f;
    
    // For each query point, find nearest neighbor in candidate
    for (size_t qi = 0; qi < querySize; ++qi) {
        const float* qHist = (*queryFeatures)[qi].histogram;
        
        float bestDist = std::numeric_limits<float>::max();
        
        for (size_t ci = 0; ci < candidateSize; ++ci) {
            const float* cHist = (*candidateFeatures)[ci].histogram;
            
            // Compute L2 squared distance between two 33-bin histograms
            float dist = 0.0f;
            for (int d = 0; d < 33; ++d) {
                float diff = qHist[d] - cHist[d];
                dist += diff * diff;
            }
            
            if (dist < bestDist) {
                bestDist = dist;
            }
        }
        
        totalMinDistance += bestDist;
        
        if (bestDist < matchThreshold) {
            ++matchedPoints;
        }
    }
    
    // Score = percentage of query points that matched
    float matchRatio = static_cast<float>(matchedPoints) / static_cast<float>(querySize);
    
    // Weight by average match quality (closer matches = higher score)
    float avgMinDist = totalMinDistance / static_cast<float>(querySize);
    float qualityFactor = std::exp(-avgMinDist / 200.0f); // Soft penalty for poor average distance
    
    float score = 100.0f * matchRatio * qualityFactor;
    
    return std::max(0.0f, std::min(100.0f, score));
}

} // namespace aum
