// FAISS Matching Engine Implementation
// Similarity search using FAISS IndexFlat with ID mapping

#include "matching.h"
#include <faiss/index_io.h>
#include <stdexcept>
#include <cmath>
#include <algorithm>

namespace aum {

MatchingIndex::MatchingIndex() {
    // Create L2 (Euclidean distance) flat index with ID mapping
    // Descriptor::dimension() = 33 for FPFH
    auto baseIndex = new faiss::IndexFlatL2(Descriptor::dimension());
    index_ = std::make_unique<faiss::IndexIDMap>(baseIndex);
}

MatchingIndex::~MatchingIndex() = default;

MatchingIndex::MatchingIndex(MatchingIndex&& other) noexcept 
    : index_(std::move(other.index_)) {}

MatchingIndex& MatchingIndex::operator=(MatchingIndex&& other) noexcept {
    if (this != &other) {
        index_ = std::move(other.index_);
    }
    return *this;
}

void MatchingIndex::add(const Descriptor& desc, int64_t id) {
    if (!index_) {
        throw std::runtime_error("Index not initialized");
    }
    
    std::vector<float> vec = desc.getAggregatedVector();
    if (vec.size() != static_cast<size_t>(Descriptor::dimension())) {
        throw std::runtime_error("Descriptor dimension mismatch");
    }
    
    // FAISS add_with_ids expects array pointers
    index_->add_with_ids(1, vec.data(), &id);
}

std::vector<MatchResult> MatchingIndex::query(const Descriptor& query, int k) {
    if (!index_) {
        throw std::runtime_error("Index not initialized");
    }
    
    if (index_->ntotal == 0) {
        return {};  // Empty index, no results
    }
    
    // Limit k to number of items in index
    int actualK = std::min(k, static_cast<int>(index_->ntotal));
    
    std::vector<float> queryVec = query.getAggregatedVector();
    if (queryVec.size() != static_cast<size_t>(Descriptor::dimension())) {
        throw std::runtime_error("Query descriptor dimension mismatch");
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
    // Recreate the index to clear it
    auto baseIndex = new faiss::IndexFlatL2(Descriptor::dimension());
    index_ = std::make_unique<faiss::IndexIDMap>(baseIndex);
}

void MatchingIndex::save(const std::string& path) const {
    if (!index_) {
        throw std::runtime_error("Cannot save: index not initialized");
    }
    faiss::write_index(index_.get(), path.c_str());
}

MatchingIndex MatchingIndex::load(const std::string& path) {
    MatchingIndex result;
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
    // Distance of 0 = 100% confidence
    // Using exponential decay: confidence = 100 * exp(-distance * scale)
    // Tuned so that distance of 0.5 gives ~60% confidence
    
    const float scale = 1.0f;  // Adjust based on typical distances
    float confidence = 100.0f * std::exp(-distance * scale);
    
    // Clamp to [0, 100]
    return std::max(0.0f, std::min(100.0f, confidence));
}

} // namespace aum
