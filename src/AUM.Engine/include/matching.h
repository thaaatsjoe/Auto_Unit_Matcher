#pragma once

#include "exports.h"
#include "descriptor.h"
#include "codebook.h"
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
 * 
 * Uses a Codebook to convert raw FPFH descriptors into histogram vectors
 * before adding to or querying the FAISS index. This makes the index
 * robust to partial scans.
 */
class AUM_API MatchingIndex {
public:
    /**
     * Create an index using a codebook for histogram-based matching.
     * @param codebook Pointer to a trained codebook (must outlive this index)
     */
    explicit MatchingIndex(const Codebook* codebook);
    
    ~MatchingIndex();
    
    // Disable copy, allow move
    MatchingIndex(const MatchingIndex&) = delete;
    MatchingIndex& operator=(const MatchingIndex&) = delete;
    MatchingIndex(MatchingIndex&& other) noexcept;
    MatchingIndex& operator=(MatchingIndex&& other) noexcept;
    
    /**
     * Add a descriptor to the index.
     * Converts to histogram via codebook before adding.
     * @param desc Descriptor to add
     * @param id Unique ID for this descriptor
     */
    void add(const Descriptor& desc, int64_t id);
    
    /**
     * Query the index for similar descriptors.
     * Converts query to histogram via codebook before searching.
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
     * @param path Path to saved index
     * @param codebook Pointer to the codebook (needed for later queries)
     */
    static MatchingIndex load(const std::string& path, const Codebook* codebook);

    /**
     * Compare two descriptors point-to-point for partial matching.
     * For each FPFH point in query, finds nearest neighbor in candidate.
     * Returns percentage of query points that match within threshold.
     * Robust to partial scans (missing geometry doesn't penalize).
     * @param query Query descriptor (e.g. partial scan)
     * @param candidate Candidate descriptor (e.g. full model from DB)
     * @return Match score 0-100%
     */
    static float compareDescriptors(const Descriptor& query, const Descriptor& candidate);

private:
    const Codebook* codebook_;
    std::unique_ptr<faiss::IndexIDMap> index_;
    
    float distanceToConfidence(float distance) const;
};

} // namespace aum
