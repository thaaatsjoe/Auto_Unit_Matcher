// FAISS Local Feature Voting + RANSAC + Dense ICP Matching Implementation
// Stage 1: FAISS IndexFlatL2 (exact search) voting on SHOT352 keypoints
// Stage 2a: RANSAC geometric gatekeeper on sparse keypoints
// Stage 2b: Dense Point-to-Plane ICP on VoxelGrid-downsampled cloud

#include "matching.h"
#include <faiss/index_io.h>
#include <pcl/registration/correspondence_estimation.h>
#include <pcl/registration/correspondence_rejection_sample_consensus.h>
#include <pcl/registration/icp.h>
#include <pcl/point_types.h>
#include <pcl/search/kdtree.h>
#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <fstream>
#include <unordered_map>
#include <unordered_set>
#include <numeric>

namespace aum {

// ============================================================================
// Construction / Move
// ============================================================================

MatchingIndex::MatchingIndex(int dimension) : dimension_(dimension) {}

MatchingIndex::~MatchingIndex() = default;

MatchingIndex::MatchingIndex(MatchingIndex&& other) noexcept
    : dimension_(other.dimension_),
      trained_(other.trained_),
      pendingVectors_(std::move(other.pendingVectors_)),
      pendingIds_(std::move(other.pendingIds_)),
      flatIndex_(std::move(other.flatIndex_)),
      idMapIndex_(std::move(other.idMapIndex_)) {
    other.trained_ = false;
}

MatchingIndex& MatchingIndex::operator=(MatchingIndex&& other) noexcept {
    if (this != &other) {
        dimension_ = other.dimension_;
        trained_ = other.trained_;
        pendingVectors_ = std::move(other.pendingVectors_);
        pendingIds_ = std::move(other.pendingIds_);
        flatIndex_ = std::move(other.flatIndex_);
        idMapIndex_ = std::move(other.idMapIndex_);
        other.trained_ = false;
    }
    return *this;
}

// ============================================================================
// Add (buffers until train)
// ============================================================================

void MatchingIndex::add(const Descriptor& desc, int64_t unitId) {
    auto features = desc.getFeatures();
    if (!features || features->empty()) return;
    
    size_t count = desc.size();
    int validCount = 0;
    
    for (size_t i = 0; i < count; ++i) {
        // Check for NaN (should already be filtered, but be safe)
        bool valid = true;
        for (int d = 0; d < dimension_; ++d) {
            if (std::isnan((*features)[i].descriptor[d])) {
                valid = false;
                break;
            }
        }
        if (!valid) continue;
        
        // Encode composite ID
        int64_t faissId = encodeId(unitId, static_cast<int>(i));
        
        // Buffer the vector
        pendingVectors_.insert(pendingVectors_.end(),
            (*features)[i].descriptor,
            (*features)[i].descriptor + dimension_);
        pendingIds_.push_back(faissId);
        validCount++;
    }
    
    // Track per-unit keypoint count for vote normalization
    unitKeypointCounts_[unitId] = validCount;
    
    // If already trained, add directly to index
    if (trained_ && idMapIndex_) {
        size_t n = pendingIds_.size();
        if (n > 0) {
            idMapIndex_->add_with_ids(
                static_cast<faiss::idx_t>(n),
                pendingVectors_.data(),
                pendingIds_.data());
            pendingVectors_.clear();
            pendingIds_.clear();
        }
    }
}

// ============================================================================
// Train Index
// ============================================================================

void MatchingIndex::trainIndex() {
    size_t n = pendingIds_.size();
    if (n == 0) {
        throw std::runtime_error("Cannot train index: no vectors added");
    }
    
    // Create IndexFlatL2 (exact exhaustive search) wrapped in IndexIDMap
    // No IVF clustering — guarantees 100% recall at the cost of O(n) search
    // This is acceptable for dental lab scale (~6000 units max)
    flatIndex_ = std::make_unique<faiss::IndexFlatL2>(dimension_);
    idMapIndex_ = std::make_unique<faiss::IndexIDMap>(flatIndex_.get());
    
    // Add all buffered vectors with IDs (no training needed for FlatL2)
    idMapIndex_->add_with_ids(
        static_cast<faiss::idx_t>(n),
        pendingVectors_.data(),
        pendingIds_.data());
    
    // Clear buffers to free memory
    pendingVectors_.clear();
    pendingVectors_.shrink_to_fit();
    pendingIds_.clear();
    pendingIds_.shrink_to_fit();
    
    trained_ = true;
}

// ============================================================================
// Query Votes (Stage 1)
// ============================================================================

std::vector<VoteResult> MatchingIndex::queryVotes(
    const Descriptor& query, int topK, int neighborsPerKeypoint) {
    
    if (!trained_ || !idMapIndex_) {
        throw std::runtime_error("Index not trained — call trainIndex() first");
    }
    
    auto features = query.getFeatures();
    if (!features || features->empty()) {
        return {};
    }
    
    size_t querySize = query.size();
    
    // Search each query keypoint against the index
    // For each query keypoint, find top-N nearest neighbors
    std::vector<float> queryVectors(querySize * dimension_);
    for (size_t i = 0; i < querySize; ++i) {
        std::copy((*features)[i].descriptor,
                  (*features)[i].descriptor + dimension_,
                  &queryVectors[i * dimension_]);
    }
    
    // FAISS batch search
    std::vector<float> distances(querySize * neighborsPerKeypoint);
    std::vector<faiss::idx_t> ids(querySize * neighborsPerKeypoint);
    
    idMapIndex_->search(
        static_cast<faiss::idx_t>(querySize),
        queryVectors.data(),
        neighborsPerKeypoint,
        distances.data(),
        ids.data());
    
    // Accumulate votes per unit ID with UNIQUE VOTE CONSTRAINT:
    // Each query keypoint can only vote ONCE per UnitId. This prevents
    // massive models (full-arch WAX-UPs) from accumulating duplicate votes
    // when multiple of their keypoints are nearest neighbors of the same
    // query keypoint. Solves size-bias at its root without normalization.
    std::unordered_map<int64_t, float> voteScores;
    std::unordered_map<int64_t, int> voteCounts;
    
    for (size_t q = 0; q < querySize; ++q) {
        std::unordered_set<int64_t> votedUnits;
        for (int k = 0; k < neighborsPerKeypoint; ++k) {
            size_t idx = q * neighborsPerKeypoint + k;
            if (ids[idx] < 0) continue;  // Invalid result
            
            int64_t unitId = decodeUnitId(ids[idx]);
            
            // Skip if this query keypoint already voted for this unit
            if (!votedUnits.insert(unitId).second) continue;
            
            float dist = distances[idx];
            // Weight: 1/(1+dist) — gentle inverse distance curve
            float weight = 1.0f / (1.0f + dist);
            
            voteScores[unitId] += weight;
            voteCounts[unitId]++;
        }
    }
    
    // NO NORMALIZATION — Stage 1 is strictly for RECALL (cast a wide net).
    // Stage 2 Dense ICP is the precision discriminator.
    // The unique vote constraint above prevents WAX-UP domination without
    // penalizing correct units that have geometry the scanner couldn't see.
    
    // Convert to sorted vector
    std::vector<VoteResult> results;
    results.reserve(voteScores.size());
    for (auto& [unitId, score] : voteScores) {
        results.push_back({unitId, score, voteCounts[unitId]});
    }
    
    // Sort by raw weighted vote score descending
    std::sort(results.begin(), results.end(),
        [](const VoteResult& a, const VoteResult& b) {
            return a.voteScore > b.voteScore;
        });
    
    // Take top K
    if (static_cast<int>(results.size()) > topK) {
        results.resize(topK);
    }
    
    return results;
}

// ============================================================================
// Geometric Verification (Stage 2): RANSAC Gatekeeper + Dense ICP
// ============================================================================

VerificationResult MatchingIndex::verify(
    const Descriptor& query,
    const Descriptor& candidate,
    float ransacThreshold,
    float icpMaxCorrespondenceDist,
    float icpFitnessDecay) {
    
    VerificationResult result = {};
    
    auto queryKeypoints = query.getKeypoints();
    auto queryFeatures = query.getFeatures();
    auto candidateKeypoints = candidate.getKeypoints();
    auto candidateFeatures = candidate.getFeatures();
    
    if (!queryKeypoints || queryKeypoints->empty() ||
        !candidateKeypoints || candidateKeypoints->empty()) {
        return result;
    }
    
    // ================================================================
    // Stage 2a: RANSAC on sparse keypoints (GATEKEEPER)
    // Purpose: Coarse transform estimation. If < 12 inliers, reject
    //          immediately without running expensive Dense ICP.
    // ================================================================
    
    // --- Build SHOT feature correspondences ---
    // For each query keypoint SHOT descriptor, find nearest in candidate
    
    size_t qSize = query.size();
    size_t cSize = candidate.size();
    
    // Build candidate SHOT matrix
    std::vector<float> candidateMatrix(cSize * Descriptor::SHOT_DIM);
    for (size_t i = 0; i < cSize; ++i) {
        std::copy((*candidateFeatures)[i].descriptor,
                  (*candidateFeatures)[i].descriptor + Descriptor::SHOT_DIM,
                  &candidateMatrix[i * Descriptor::SHOT_DIM]);
    }
    
    // For each query SHOT, find nearest candidate SHOT (brute-force L2)
    // Apply Lowe's Ratio Test: reject if best/secondBest > 0.8
    // Lowe's ratio test: bestDist < 0.8 * secondBestDist on squared L2
    // For 352-D SHOT descriptors, 0.8 on squared distances (effective ratio ~0.89)
    // is appropriate — high-dimensional spaces need MORE lenient thresholds
    constexpr float RATIO_THRESHOLD = 0.8f;
    pcl::Correspondences correspondences;
    for (size_t qi = 0; qi < qSize; ++qi) {
        float bestDist = std::numeric_limits<float>::max();
        float secondBestDist = std::numeric_limits<float>::max();
        int bestIdx = -1;
        
        const float* qDesc = (*queryFeatures)[qi].descriptor;
        for (size_t ci = 0; ci < cSize; ++ci) {
            float dist = 0.0f;
            const float* cDesc = &candidateMatrix[ci * Descriptor::SHOT_DIM];
            for (int d = 0; d < Descriptor::SHOT_DIM; ++d) {
                float diff = qDesc[d] - cDesc[d];
                dist += diff * diff;
            }
            if (dist < bestDist) {
                secondBestDist = bestDist;
                bestDist = dist;
                bestIdx = static_cast<int>(ci);
            } else if (dist < secondBestDist) {
                secondBestDist = dist;
            }
        }
        
        // Lowe's Ratio Test: only accept if best match is significantly
        // better than second-best (i.e., the feature is distinctive)
        if (bestIdx >= 0 && bestDist < RATIO_THRESHOLD * secondBestDist) {
            pcl::Correspondence corr;
            corr.index_query = static_cast<int>(qi);
            corr.index_match = bestIdx;
            corr.distance = bestDist;
            correspondences.push_back(corr);
        }
    }
    
    result.correspondences = static_cast<int>(correspondences.size());
    if (correspondences.empty()) {
        return result;
    }
    
    // --- RANSAC outlier rejection ---
    pcl::registration::CorrespondenceRejectorSampleConsensus<pcl::PointXYZ> ransac;
    ransac.setInputSource(queryKeypoints);
    ransac.setInputTarget(candidateKeypoints);
    ransac.setInlierThreshold(ransacThreshold);
    ransac.setMaximumIterations(1000);
    ransac.setInputCorrespondences(
        std::make_shared<pcl::Correspondences>(correspondences));
    
    pcl::Correspondences inlierCorrespondences;
    ransac.getCorrespondences(inlierCorrespondences);
    
    result.ransacInliers = static_cast<int>(inlierCorrespondences.size());
    result.ransacInlierRatio = correspondences.empty() ? 0.0f :
        static_cast<float>(inlierCorrespondences.size()) / 
        static_cast<float>(correspondences.size());
    
    // GATEKEEPER: Minimum 3 inliers (minimum for rigid body estimation)
    // We trust Dense ICP as the real discriminator — RANSAC provides initial
    // alignment seed. Even a noisy 3-point seed lets ICP converge for correct
    // matches (surface anatomy locks in) but diverge for wrong matches.
    // Lowered from 12 to support partial scans with fewer keypoints.
    if (inlierCorrespondences.size() < 3) {
        result.finalScore = 0.0f;
        result.icpFitnessScore = std::numeric_limits<float>::max();
        return result;
    }
    
    // Get the RANSAC transformation (seed for Dense ICP)
    Eigen::Matrix4f ransacTransform = ransac.getBestTransformation();
    
    // ================================================================
    // Stage 2b: Dense Point-to-Plane ICP
    // Uses the full VoxelGrid-downsampled cloud (thousands of points)
    // with normals computed at sharp radius (0.5mm) for micro-anatomy.
    //
    // CRITICAL GUARDRAILS:
    // 1. Source = Scan (partial, exterior only)
    //    Target = DB STL (full, exterior + intaglio)
    //    Reversing this evaluates hidden intaglio → massive error → fail
    //
    // 2. MaxCorrespondenceDistance = 0.5mm
    //    Prevents pairing scan edges with intaglio cavity points
    //
    // 3. Point-to-Plane ICP (not Point-to-Point)
    //    Point-to-Point slides on smooth organic surfaces
    //    Point-to-Plane locks into cusp/fissure topology
    // ================================================================
    
    // Check both descriptors have dense clouds
    if (!query.hasDenseCloud() || !candidate.hasDenseCloud()) {
        // Fallback: sparse ICP (legacy SH01 data, rare)
        pcl::IterativeClosestPoint<pcl::PointXYZ, pcl::PointXYZ> icp;
        icp.setInputSource(queryKeypoints);
        icp.setInputTarget(candidateKeypoints);
        icp.setMaxCorrespondenceDistance(icpMaxCorrespondenceDist);
        icp.setMaximumIterations(50);
        icp.setTransformationEpsilon(1e-8);
        icp.setEuclideanFitnessEpsilon(1e-6);
        
        PointCloud aligned;
        icp.align(aligned, ransacTransform);
        
        result.icpFitnessScore = static_cast<float>(icp.getFitnessScore());
        float icpQuality = std::exp(-result.icpFitnessScore / icpFitnessDecay);
        result.finalScore = std::max(0.0f, std::min(100.0f, 100.0f * icpQuality));
        return result;
    }
    
    // --- Build PointNormal clouds for Point-to-Plane ICP ---
    auto queryDenseCloud = query.getDenseCloud();
    auto queryDenseNormals = query.getDenseNormals();
    auto candidateDenseCloud = candidate.getDenseCloud();
    auto candidateDenseNormals = candidate.getDenseNormals();
    
    // Combine XYZ + Normals into PointNormal type for ICP
    auto sourceCloud = std::make_shared<PointNormalCloud>();
    sourceCloud->resize(queryDenseCloud->size());
    for (size_t i = 0; i < queryDenseCloud->size(); ++i) {
        (*sourceCloud)[i].x = (*queryDenseCloud)[i].x;
        (*sourceCloud)[i].y = (*queryDenseCloud)[i].y;
        (*sourceCloud)[i].z = (*queryDenseCloud)[i].z;
        (*sourceCloud)[i].normal_x = (*queryDenseNormals)[i].normal_x;
        (*sourceCloud)[i].normal_y = (*queryDenseNormals)[i].normal_y;
        (*sourceCloud)[i].normal_z = (*queryDenseNormals)[i].normal_z;
        (*sourceCloud)[i].curvature = (*queryDenseNormals)[i].curvature;
    }
    
    auto targetCloud = std::make_shared<PointNormalCloud>();
    targetCloud->resize(candidateDenseCloud->size());
    for (size_t i = 0; i < candidateDenseCloud->size(); ++i) {
        (*targetCloud)[i].x = (*candidateDenseCloud)[i].x;
        (*targetCloud)[i].y = (*candidateDenseCloud)[i].y;
        (*targetCloud)[i].z = (*candidateDenseCloud)[i].z;
        (*targetCloud)[i].normal_x = (*candidateDenseNormals)[i].normal_x;
        (*targetCloud)[i].normal_y = (*candidateDenseNormals)[i].normal_y;
        (*targetCloud)[i].normal_z = (*candidateDenseNormals)[i].normal_z;
        (*targetCloud)[i].curvature = (*candidateDenseNormals)[i].curvature;
    }
    
    // --- Point-to-Plane ICP ---
    // GUARDRAIL 1: Source = scan (partial), Target = DB STL (full)
    // GUARDRAIL 2: MaxCorrespondenceDistance = 0.5mm (pull-through prevention)
    // GUARDRAIL 3: Point-to-Plane (IterativeClosestPointWithNormals)
    pcl::IterativeClosestPointWithNormals<pcl::PointNormal, pcl::PointNormal> icp;
    icp.setInputSource(sourceCloud);     // Scan = SOURCE (partial, exterior)
    icp.setInputTarget(targetCloud);     // DB STL = TARGET (full, exterior+intaglio)
    icp.setMaxCorrespondenceDistance(icpMaxCorrespondenceDist);  // 0.5mm
    icp.setMaximumIterations(50);
    icp.setTransformationEpsilon(1e-8);
    icp.setEuclideanFitnessEpsilon(1e-6);
    
    PointNormalCloud aligned;
    icp.align(aligned, ransacTransform);  // Use RANSAC transform as initial seed
    
    result.icpFitnessScore = static_cast<float>(icp.getFitnessScore());
    
    // ================================================================
    // Scoring: ICP fitness is the SOLE discriminator
    // Score = 100 * exp(-fitness / icpFitnessDecay)
    // icpFitnessDecay is configurable (default 0.5) — tunable from
    // C# UI without recompiling C++ engine
    //
    // Dense Point-to-Plane ICP expected values:
    //   Correct match:  fitness 0.1–0.3mm²  → score 55–82%
    //   Marginal match: fitness 0.3–0.6mm²  → score 30–55%
    //   Wrong match:    fitness > 1.0mm²     → score < 14%
    // ================================================================
    
    // RANSAC QUALITY PENALTY:
    // When inlier ratio approaches 100%, the RANSAC transform is degenerate —
    // all SHOT correspondences fell near the centroid and no outlier rejection
    // occurred. Healthy geometric matches have 2-30% inlier ratio.
    // Penalty ramp: ratio 0-0.80 = no penalty, 0.80-1.00 = linear to 0.2x
    float degeneratePenalty = 1.0f;
    if (result.ransacInlierRatio > 0.80f) {
        degeneratePenalty = 1.0f - 0.8f * ((result.ransacInlierRatio - 0.80f) / 0.20f);
        degeneratePenalty = std::max(0.2f, degeneratePenalty);
    }
    
    float icpQuality = std::exp(-result.icpFitnessScore / icpFitnessDecay);
    result.finalScore = std::max(0.0f, std::min(100.0f, 100.0f * icpQuality * degeneratePenalty));
    
    return result;
}

// ============================================================================
// Size / Clear
// ============================================================================

size_t MatchingIndex::size() const {
    size_t total = pendingIds_.size();
    if (idMapIndex_) {
        total += static_cast<size_t>(idMapIndex_->ntotal);
    }
    return total;
}

void MatchingIndex::clear() {
    pendingVectors_.clear();
    pendingIds_.clear();
    unitKeypointCounts_.clear();
    flatIndex_.reset();
    idMapIndex_.reset();
    trained_ = false;
}

// ============================================================================
// Save / Load
// ============================================================================

void MatchingIndex::save(const std::string& path) const {
    if (!trained_ || !idMapIndex_) {
        throw std::runtime_error("Cannot save untrained index");
    }
    faiss::write_index(idMapIndex_.get(), path.c_str());
}

MatchingIndex MatchingIndex::load(const std::string& path) {
    auto* rawIndex = faiss::read_index(path.c_str());
    auto* idMap = dynamic_cast<faiss::IndexIDMap*>(rawIndex);
    if (!idMap) {
        delete rawIndex;
        throw std::runtime_error("Loaded index is not IndexIDMap");
    }
    
    MatchingIndex result(idMap->d);
    result.idMapIndex_.reset(idMap);
    // The flat index is owned by the IDMap after loading
    result.flatIndex_.reset(); // Don't double-own it
    result.trained_ = true;
    
    return result;
}

} // namespace aum
