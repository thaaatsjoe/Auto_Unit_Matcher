#pragma once

#include "exports.h"
#include "descriptor.h"
#include <faiss/IndexFlat.h>
#include <faiss/IndexIDMap.h>
#include <memory>
#include <vector>
#include <unordered_map>

namespace aum {

/**
 * Voting result from Stage 1 FAISS search.
 */
struct AUM_API VoteResult {
    int64_t unitId;       // Database ID of the unit
    float   voteScore;    // Weighted vote score (sum of 1/(1+dist))
    int     voteCount;    // Raw number of keypoint votes
};

/**
 * Geometric verification result from Stage 2 RANSAC + Dense ICP.
 */
struct AUM_API VerificationResult {
    int64_t unitId;
    float   ransacInlierRatio;     // Fraction of correspondences that are inliers
    float   icpFitnessScore;       // Dense ICP fitness (mean squared distance)
    float   finalScore;            // ICP-based confidence score 0-100
    int     ransacInliers;         // Number of RANSAC inliers
    int     correspondences;       // Total correspondences before RANSAC
};

/**
 * Match result from similarity search (legacy-compatible).
 */
struct AUM_API MatchResult {
    int64_t id;           // Database ID
    float distance;       // Distance metric
    float confidence;     // Confidence percentage (0-100)
};

// ID encoding: faiss_id = unitId * MAX_KP + keypointIndex
static constexpr int64_t MAX_KEYPOINTS_PER_UNIT = 10000;

/**
 * FAISS-based Local Feature Voting index with Dense ICP verification.
 * 
 * Stage 1 (Voting):
 *   Indexes all ISS/SHOT keypoints in a FAISS IndexFlatL2 (exact search).
 *   Wrapped in IndexIDMap for unit ID preservation.
 *   At query time, each scan keypoint votes for its nearest unit.
 *   Top-K voted units proceed to Stage 2.
 * 
 * Stage 2 (Geometric Verification):
 *   Stage 2a: RANSAC on sparse keypoints — coarse gatekeeper (≥12 inliers or reject).
 *   Stage 2b: Dense Point-to-Plane ICP on full downsampled cloud.
 *             Uses RANSAC transform as seed. ICP fitness = final confidence.
 */
class AUM_API MatchingIndex {
public:
    /**
     * Create an index for SHOT352 descriptors.
     * @param dimension Feature dimension (352 for SHOT352)
     */
    explicit MatchingIndex(int dimension = Descriptor::SHOT_DIM);
    
    ~MatchingIndex();
    
    // Disable copy, allow move
    MatchingIndex(const MatchingIndex&) = delete;
    MatchingIndex& operator=(const MatchingIndex&) = delete;
    MatchingIndex(MatchingIndex&&) noexcept;
    MatchingIndex& operator=(MatchingIndex&&) noexcept;
    
    /**
     * Add all keypoints from a descriptor to the index.
     * Buffers vectors until trainIndex() is called.
     * @param desc Descriptor with keypoints + SHOT features
     * @param unitId Database ID of the unit
     */
    void add(const Descriptor& desc, int64_t unitId);
    
    /**
     * Finalize the index and add all buffered vectors.
     * Must be called after all add() calls and before any query.
     * Uses IndexFlatL2 (exact search) wrapped in IndexIDMap.
     */
    void trainIndex();
    
    /**
     * Query the index using voting.
     * Each query keypoint searches its top-K nearest neighbors in FAISS,
     * decodes the unit IDs, and accumulates weighted votes.
     * @param query Query descriptor (from scan)
     * @param topK Number of top-voted units to return
     * @param neighborsPerKeypoint How many FAISS neighbors per query keypoint (default 100)
     * @return Sorted vector of VoteResult (highest vote score first)
     */
    std::vector<VoteResult> queryVotes(const Descriptor& query, int topK = 10, int neighborsPerKeypoint = 100);
    
    /**
     * Stage 2: Geometric verification using RANSAC + Dense Point-to-Plane ICP.
     * 
     * Stage 2a: RANSAC on sparse keypoints for coarse transform estimation.
     *           If inliers < 12, returns score 0.0 immediately (gatekeeper).
     * Stage 2b: Dense Point-to-Plane ICP using RANSAC transform as seed.
     *           Scan = Source (partial), DB STL = Target (full).
     *           MaxCorrespondenceDistance = 0.5mm (prevents pull-through to intaglio).
     * 
     * @param query Query descriptor (scan — partial, exterior only)
     * @param candidate Candidate descriptor (database unit — full STL)
     * @param ransacThreshold RANSAC inlier distance threshold (mm)
     * @param icpMaxCorrespondenceDist Dense ICP max correspondence distance (mm)
     * @param icpFitnessDecay Decay constant for exp(-fitness/decay) scoring
     * @return VerificationResult with RANSAC inliers, ICP fitness, final score
     */
    static VerificationResult verify(
        const Descriptor& query,
        const Descriptor& candidate,
        float ransacThreshold = 0.25f,
        float icpMaxCorrespondenceDist = 0.5f,
        float icpFitnessDecay = 0.5f);
    
    /**
     * Get the total number of feature vectors in the index.
     */
    size_t size() const;
    
    /**
     * Check if index has been trained.
     */
    bool isTrained() const { return trained_; }
    
    /**
     * Clear all data and reset to pre-training state.
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
    int dimension_;
    bool trained_ = false;
    
    // Pre-training buffers
    std::vector<float> pendingVectors_;
    std::vector<int64_t> pendingIds_;
    
    // FAISS index components (created during trainIndex)
    std::unique_ptr<faiss::IndexFlatL2> flatIndex_;
    std::unique_ptr<faiss::IndexIDMap> idMapIndex_;
    
    // Decode composite FAISS ID
    static int64_t encodeId(int64_t unitId, int keypointIdx) {
        return unitId * MAX_KEYPOINTS_PER_UNIT + keypointIdx;
    }
    static int64_t decodeUnitId(int64_t faissId) {
        return faissId / MAX_KEYPOINTS_PER_UNIT;
    }
    static int decodeKeypointIdx(int64_t faissId) {
        return static_cast<int>(faissId % MAX_KEYPOINTS_PER_UNIT);
    }
};

} // namespace aum
