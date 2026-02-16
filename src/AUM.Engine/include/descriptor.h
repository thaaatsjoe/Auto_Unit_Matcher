#pragma once

#include "exports.h"
#include <pcl/point_types.h>
#include <pcl/point_cloud.h>
#include <pcl/features/shot.h>
#include <vector>
#include <memory>

namespace aum {

// ============================================================================
// Type aliases
// ============================================================================
using PointCloud = pcl::PointCloud<pcl::PointXYZ>;
using PointCloudPtr = pcl::PointCloud<pcl::PointXYZ>::Ptr;
using NormalCloud = pcl::PointCloud<pcl::Normal>;
using NormalCloudPtr = pcl::PointCloud<pcl::Normal>::Ptr;
using SHOTSignature = pcl::SHOT352;
using SHOTCloud = pcl::PointCloud<pcl::SHOT352>;
using SHOTCloudPtr = pcl::PointCloud<pcl::SHOT352>::Ptr;

// ============================================================================
// Normal+Point combined type for Point-to-Plane ICP
// ============================================================================
using PointNormal = pcl::PointNormal;
using PointNormalCloud = pcl::PointCloud<pcl::PointNormal>;
using PointNormalCloudPtr = pcl::PointCloud<pcl::PointNormal>::Ptr;

/**
 * Descriptor extraction configuration.
 * All radii are in millimeters (1:1 patient scale, no sintering compensation).
 */
struct AUM_API DescriptorConfig {
    // Downsampling
    float voxelSize = 0.2f;           // Smooths 50-micron milling noise, preserves macro anatomy
    
    // Normal estimation (for SHOT features — surface-bound, no bleed-through)
    float normalRadius = 0.5f;        // ≤crown thickness — prevents LRF flip from intaglio
    
    // ISS Keypoint Detection
    float issSalientRadius = 1.0f;    // Captures cusp/pit scale features
    float issNonMaxRadius = 0.6f;     // Forces spatial distribution of keypoints
    float issThreshold21 = 0.85f;     // Accept surface variation
    float issThreshold32 = 0.85f;     // Accept surface variation
    int   issMinNeighbors = 5;        // ISS minimum neighbors
    int   maxKeypoints = 2000;        // High cap — ISS non-max radius distributes naturally
    
    // SHOT descriptor
    float shotRadius = 1.0f;          // < crown thickness — prevents Thin Shell penetration
    
    // Dense ICP normals (separate from SHOT normals — sharper for micro-anatomy)
    float icpNormalRadius = 0.5f;     // ~2.5x voxelSize — preserves cusp/fissure sharpness
    
    // ICP scoring
    float icpFitnessDecay = 0.5f;     // Decay constant for exp(-fitness/decay) score formula
                                       // Dense ICP produces fitness ~0.1-0.5 for correct matches
                                       // Tunable from C# without recompiling C++
};

/**
 * Container for extracted ISS keypoints + SHOT352 descriptors + dense cloud.
 * 
 * Stores:
 *   - ISS keypoint positions (for RANSAC coarse alignment + FAISS voting)
 *   - SHOT352 features at each keypoint (for FAISS voting)
 *   - VoxelGrid-downsampled dense cloud + normals (for Dense ICP verification)
 * 
 * Blob format (v3 "SH02"):
 *   [magic: 4 bytes = "SH02"]
 *   [keypointCount: uint32]
 *   [denseCount: uint32]
 *   [keypoints: keypointCount * 3 * float32]          // XYZ positions
 *   [shot_data: keypointCount * 352 * float32]        // SHOT descriptors
 *   [denseXYZ: denseCount * 3 * float32]              // Dense cloud XYZ
 *   [denseNormals: denseCount * 4 * float32]          // nx, ny, nz, curvature
 */
class AUM_API Descriptor {
public:
    static constexpr int SHOT_DIM = 352;
    static constexpr char MAGIC_V1[4] = {'S', 'H', '0', '1'};  // Legacy (no dense cloud)
    static constexpr char MAGIC_V2[4] = {'S', 'H', '0', '2'};  // Current (with dense cloud)
    
    Descriptor() = default;
    
    /** Construct with keypoints + SHOT only (legacy, no dense cloud). */
    Descriptor(PointCloudPtr keypoints, SHOTCloudPtr features);
    
    /** Construct with keypoints + SHOT + dense cloud + normals. */
    Descriptor(PointCloudPtr keypoints, SHOTCloudPtr features,
               PointCloudPtr denseCloud, NormalCloudPtr denseNormals);
    
    /** Get the keypoint XYZ positions. */
    PointCloudPtr getKeypoints() const { return keypoints_; }
    
    /** Get the SHOT352 features at each keypoint. */
    SHOTCloudPtr getFeatures() const { return features_; }
    
    /** Get the VoxelGrid-downsampled dense cloud (for ICP). */
    PointCloudPtr getDenseCloud() const { return denseCloud_; }
    
    /** Get the normals for the dense cloud (for Point-to-Plane ICP). */
    NormalCloudPtr getDenseNormals() const { return denseNormals_; }
    
    /** Check if dense cloud is available (SH02 format). */
    bool hasDenseCloud() const { return denseCloud_ && !denseCloud_->empty(); }
    
    /** Get number of valid keypoints. */
    size_t size() const;
    
    /** Get number of dense cloud points. */
    size_t denseSize() const;
    
    /** Get descriptor dimension for FAISS. */
    static constexpr int dimension() { return SHOT_DIM; }
    
    /** Serialize to binary blob (SH02: XYZ + SHOT + denseCloud + normals). */
    std::vector<uint8_t> serialize() const;
    
    /** Deserialize from binary blob. Supports both SH01 and SH02. */
    static Descriptor deserialize(const uint8_t* data, size_t len);

private:
    PointCloudPtr keypoints_;        // XYZ positions of ISS keypoints (sparse)
    SHOTCloudPtr features_;          // SHOT352 at each keypoint
    PointCloudPtr denseCloud_;       // VoxelGrid-downsampled full surface (for ICP)
    NormalCloudPtr denseNormals_;    // Normals at each dense point (for Point-to-Plane ICP)
};

/**
 * ISS Keypoint + SHOT352 descriptor extractor.
 * Pipeline: Downsample → SHOT Normals → ISS Keypoints → SHOT352
 *                      → ICP Normals (separate radius)
 * Output includes both sparse keypoints/SHOT and the dense downsampled cloud.
 */
class AUM_API DescriptorExtractor {
public:
    explicit DescriptorExtractor(const DescriptorConfig& config = {});
    
    /**
     * Extract ISS keypoints, SHOT352 descriptors, and dense cloud from point cloud.
     * @param cloud Input point cloud (STL mesh vertices)
     * @return Descriptor containing keypoint positions + SHOT features + dense cloud
     */
    Descriptor extract(PointCloudPtr cloud);

private:
    DescriptorConfig config_;
    
    PointCloudPtr downsample(PointCloudPtr cloud);
    NormalCloudPtr estimateNormals(PointCloudPtr cloud, float radius);
    PointCloudPtr detectKeypoints(PointCloudPtr cloud, NormalCloudPtr normals);
};

} // namespace aum
