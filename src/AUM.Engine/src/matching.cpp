// FAISS Matching Index Implementation
// Phase 1 will add index optimization and persistence

#include "matching.h"
#include <faiss/index_io.h>
#include <cmath>
#include <algorithm>
#include <stdexcept>

namespace aum {

MatchingIndex::MatchingIndex() {
    // Create a flat L2 index with ID mapping
    auto flatIndex = new faiss::IndexFlatL2(Descriptor::dimension());
    index_ = std::make_unique<faiss::IndexIDMap>(flatIndex);
}

MatchingIndex::~MatchingIndex() = default;

MatchingIndex::MatchingIndex(MatchingIndex&&) noexcept = default;
MatchingIndex& MatchingIndex::operator=(MatchingIndex&&) noexcept = default;

void MatchingIndex::add(const Descriptor& desc, int64_t id) {
    auto vec = desc.getAggregatedVector();
    if (vec.empty()) {
        throw std::runtime_error("Cannot add empty descriptor");
    }
    
    index_->add_with_ids(1, vec.data(), &id);
}

std::vector<MatchResult> MatchingIndex::query(const Descriptor& query, int k) {
    if (index_->ntotal == 0) {
        return {};
    }
    
    auto vec = query.getAggregatedVector();
    if (vec.empty()) {
        throw std::runtime_error("Cannot query with empty descriptor");
    }
    
    // Limit k to available entries
    int actualK = std::min(k, static_cast<int>(index_->ntotal));
    
    std::vector<float> distances(actualK);
    std::vector<faiss::idx_t> ids(actualK);
    
    index_->search(1, vec.data(), actualK, distances.data(), ids.data());
    
    std::vector<MatchResult> results;
    results.reserve(actualK);
    
    for (int i = 0; i < actualK; ++i) {
        if (ids[i] >= 0) { // Valid ID
            results.push_back({
                ids[i],
                distances[i],
                distanceToConfidence(distances[i])
            });
        }
    }
    
    return results;
}

float MatchingIndex::distanceToConfidence(float distance) const {
    // Convert L2 distance to confidence percentage
    // This is a simple exponential decay - tune in Phase 1
    // Distance of 0 = 100% confidence
    // Distance of 1 = ~37% confidence
    // Distance of 2 = ~14% confidence
    float confidence = 100.0f * std::exp(-distance);
    return std::clamp(confidence, 0.0f, 100.0f);
}

size_t MatchingIndex::size() const {
    return static_cast<size_t>(index_->ntotal);
}

void MatchingIndex::clear() {
    index_->reset();
}

void MatchingIndex::save(const std::string& path) const {
    faiss::write_index(index_.get(), path.c_str());
}

MatchingIndex MatchingIndex::load(const std::string& path) {
    MatchingIndex result;
    result.index_.reset(dynamic_cast<faiss::IndexIDMap*>(faiss::read_index(path.c_str())));
    if (!result.index_) {
        throw std::runtime_error("Failed to load index from: " + path);
    }
    return result;
}

} // namespace aum
