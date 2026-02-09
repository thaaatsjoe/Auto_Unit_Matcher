#pragma once

#include "exports.h"
#include "descriptor.h"
#include <faiss/IndexFlat.h>
#include <faiss/IndexIDMap.h>
#include <memory>
#include <vector>

namespace aum {

/**
 * Match result from similarity search.
 */
struct AUM_API MatchResult {
    int64_t id;           // Database ID
    float distance;       // L2 distance (lower = better)
    float confidence;     // Confidence percentage (0-100)
};

/**
 * FAISS-based similarity search index.
 */
class AUM_API MatchingIndex {
public:
    MatchingIndex();
    ~MatchingIndex();
    
    // Disable copy, allow move
    MatchingIndex(const MatchingIndex&) = delete;
    MatchingIndex& operator=(const MatchingIndex&) = delete;
    MatchingIndex(MatchingIndex&&) noexcept;
    MatchingIndex& operator=(MatchingIndex&&) noexcept;
    
    /**
     * Add a descriptor to the index.
     * @param desc Descriptor to add
     * @param id Unique ID for this descriptor
     */
    void add(const Descriptor& desc, int64_t id);
    
    /**
     * Query the index for similar descriptors.
     * @param query Query descriptor
     * @param k Number of results to return
     * @return Vector of match results, sorted by distance
     */
    std::vector<MatchResult> query(const Descriptor& query, int k = 5);
    
    /**
     * Get the number of descriptors in the index.
     */
    size_t size() const;
    
    /**
     * Clear all descriptors from the index.
     */
    void clear();
    
    /**
     * Save index to file.
     */
    void save(const std::string& path) const;
    
    /**
     * Load index from file.
     */
    static MatchingIndex load(const std::string& path);

private:
    std::unique_ptr<faiss::IndexIDMap> index_;
    
    float distanceToConfidence(float distance) const;
};

} // namespace aum
