// ISS Keypoint + SHOT352 Descriptor Extraction Implementation
// Pipeline: Downsample → SHOT Normals → ISS Keypoints → SHOT352
//                       → ICP Normals (separate radius for micro-anatomy)
// No scaling applied — STLs are 1:1 patient scale

#include "descriptor.h"
#include <pcl/keypoints/iss_3d.h>
#include <pcl/features/shot_omp.h>
#include <pcl/features/normal_3d_omp.h>
#include <pcl/filters/voxel_grid.h>
#include <pcl/search/kdtree.h>
#include <stdexcept>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <numeric>

namespace aum {

// ============================================================================
// Descriptor Implementation
// ============================================================================

Descriptor::Descriptor(PointCloudPtr keypoints, SHOTCloudPtr features)
    : keypoints_(keypoints), features_(features) {}

Descriptor::Descriptor(PointCloudPtr keypoints, SHOTCloudPtr features,
                       PointCloudPtr denseCloud, NormalCloudPtr denseNormals)
    : keypoints_(keypoints), features_(features),
      denseCloud_(denseCloud), denseNormals_(denseNormals) {}

size_t Descriptor::size() const {
    if (!keypoints_ || !features_) return 0;
    return std::min(keypoints_->size(), features_->size());
}

size_t Descriptor::denseSize() const {
    if (!denseCloud_) return 0;
    return denseCloud_->size();
}

// ============================================================================
// Serialization — SH02 format
// ============================================================================

std::vector<uint8_t> Descriptor::serialize() const {
    size_t kpCount = size();
    if (kpCount == 0) return {};
    
    size_t dCount = denseSize();
    
    // SH02 format:
    //   [magic: 4B "SH02"]
    //   [keypointCount: uint32]
    //   [denseCount: uint32]
    //   [keypoints: kpCount * 3 * float32]
    //   [shot_data: kpCount * 352 * float32]
    //   [denseXYZ: dCount * 3 * float32]
    //   [denseNormals: dCount * 4 * float32]   // nx, ny, nz, curvature
    
    size_t kpXyzBytes = kpCount * 3 * sizeof(float);
    size_t shotBytes = kpCount * SHOT_DIM * sizeof(float);
    size_t denseXyzBytes = dCount * 3 * sizeof(float);
    size_t denseNormalBytes = dCount * 4 * sizeof(float);  // nx, ny, nz, curvature
    
    size_t totalSize = 4 + 4 + 4 + kpXyzBytes + shotBytes + denseXyzBytes + denseNormalBytes;
    
    std::vector<uint8_t> blob(totalSize);
    uint8_t* ptr = blob.data();
    
    // Write magic "SH02"
    std::memcpy(ptr, MAGIC_V2, 4);
    ptr += 4;
    
    // Write keypoint count
    uint32_t kpU32 = static_cast<uint32_t>(kpCount);
    std::memcpy(ptr, &kpU32, 4);
    ptr += 4;
    
    // Write dense count
    uint32_t dU32 = static_cast<uint32_t>(dCount);
    std::memcpy(ptr, &dU32, 4);
    ptr += 4;
    
    // Write keypoint XYZ positions
    for (size_t i = 0; i < kpCount; ++i) {
        float xyz[3] = {
            (*keypoints_)[i].x,
            (*keypoints_)[i].y,
            (*keypoints_)[i].z
        };
        std::memcpy(ptr, xyz, 3 * sizeof(float));
        ptr += 3 * sizeof(float);
    }
    
    // Write SHOT descriptors
    for (size_t i = 0; i < kpCount; ++i) {
        std::memcpy(ptr, (*features_)[i].descriptor, SHOT_DIM * sizeof(float));
        ptr += SHOT_DIM * sizeof(float);
    }
    
    // Write dense cloud XYZ
    for (size_t i = 0; i < dCount; ++i) {
        float xyz[3] = {
            (*denseCloud_)[i].x,
            (*denseCloud_)[i].y,
            (*denseCloud_)[i].z
        };
        std::memcpy(ptr, xyz, 3 * sizeof(float));
        ptr += 3 * sizeof(float);
    }
    
    // Write dense normals (nx, ny, nz, curvature)
    for (size_t i = 0; i < dCount; ++i) {
        float ndata[4] = {
            (*denseNormals_)[i].normal_x,
            (*denseNormals_)[i].normal_y,
            (*denseNormals_)[i].normal_z,
            (*denseNormals_)[i].curvature
        };
        std::memcpy(ptr, ndata, 4 * sizeof(float));
        ptr += 4 * sizeof(float);
    }
    
    return blob;
}

// ============================================================================
// Deserialization — supports both SH01 (legacy) and SH02 (current)
// ============================================================================

Descriptor Descriptor::deserialize(const uint8_t* data, size_t len) {
    if (len < 8) {
        throw std::runtime_error("Invalid descriptor blob: too short");
    }
    
    // Detect format version by magic
    bool isSH02 = (std::memcmp(data, MAGIC_V2, 4) == 0);
    bool isSH01 = (std::memcmp(data, MAGIC_V1, 4) == 0);
    
    if (!isSH01 && !isSH02) {
        throw std::runtime_error("Invalid descriptor blob: unknown magic (expected SH01 or SH02)");
    }
    
    if (isSH01) {
        // ---- Legacy SH01 format: [magic:4][count:4][kpXYZ][SHOT] ----
        uint32_t count;
        std::memcpy(&count, data + 4, 4);
        
        size_t xyzBytes = count * 3 * sizeof(float);
        size_t shotBytes = count * SHOT_DIM * sizeof(float);
        size_t expectedSize = 4 + 4 + xyzBytes + shotBytes;
        
        if (len < expectedSize) {
            throw std::runtime_error("Invalid SH01 blob: size mismatch");
        }
        
        const uint8_t* ptr = data + 8;
        
        auto keypoints = std::make_shared<PointCloud>();
        keypoints->resize(count);
        for (size_t i = 0; i < count; ++i) {
            float xyz[3];
            std::memcpy(xyz, ptr, 3 * sizeof(float));
            ptr += 3 * sizeof(float);
            (*keypoints)[i].x = xyz[0];
            (*keypoints)[i].y = xyz[1];
            (*keypoints)[i].z = xyz[2];
        }
        
        auto features = std::make_shared<SHOTCloud>();
        features->resize(count);
        for (size_t i = 0; i < count; ++i) {
            std::memcpy((*features)[i].descriptor, ptr, SHOT_DIM * sizeof(float));
            ptr += SHOT_DIM * sizeof(float);
        }
        
        // No dense cloud in SH01
        return Descriptor(keypoints, features);
    }
    
    // ---- SH02 format: [magic:4][kpCount:4][denseCount:4][kpXYZ][SHOT][denseXYZ][denseNormals] ----
    if (len < 12) {
        throw std::runtime_error("Invalid SH02 blob: too short for header");
    }
    
    uint32_t kpCount, denseCount;
    std::memcpy(&kpCount, data + 4, 4);
    std::memcpy(&denseCount, data + 8, 4);
    
    size_t kpXyzBytes = kpCount * 3 * sizeof(float);
    size_t shotBytes = kpCount * SHOT_DIM * sizeof(float);
    size_t denseXyzBytes = denseCount * 3 * sizeof(float);
    size_t denseNormalBytes = denseCount * 4 * sizeof(float);
    size_t expectedSize = 4 + 4 + 4 + kpXyzBytes + shotBytes + denseXyzBytes + denseNormalBytes;
    
    if (len < expectedSize) {
        throw std::runtime_error("Invalid SH02 blob: size mismatch");
    }
    
    const uint8_t* ptr = data + 12;
    
    // Read keypoint positions
    auto keypoints = std::make_shared<PointCloud>();
    keypoints->resize(kpCount);
    for (size_t i = 0; i < kpCount; ++i) {
        float xyz[3];
        std::memcpy(xyz, ptr, 3 * sizeof(float));
        ptr += 3 * sizeof(float);
        (*keypoints)[i].x = xyz[0];
        (*keypoints)[i].y = xyz[1];
        (*keypoints)[i].z = xyz[2];
    }
    
    // Read SHOT descriptors
    auto features = std::make_shared<SHOTCloud>();
    features->resize(kpCount);
    for (size_t i = 0; i < kpCount; ++i) {
        std::memcpy((*features)[i].descriptor, ptr, SHOT_DIM * sizeof(float));
        ptr += SHOT_DIM * sizeof(float);
    }
    
    // Read dense cloud XYZ
    auto denseCloud = std::make_shared<PointCloud>();
    denseCloud->resize(denseCount);
    for (size_t i = 0; i < denseCount; ++i) {
        float xyz[3];
        std::memcpy(xyz, ptr, 3 * sizeof(float));
        ptr += 3 * sizeof(float);
        (*denseCloud)[i].x = xyz[0];
        (*denseCloud)[i].y = xyz[1];
        (*denseCloud)[i].z = xyz[2];
    }
    
    // Read dense normals (nx, ny, nz, curvature)
    auto denseNormals = std::make_shared<NormalCloud>();
    denseNormals->resize(denseCount);
    for (size_t i = 0; i < denseCount; ++i) {
        float ndata[4];
        std::memcpy(ndata, ptr, 4 * sizeof(float));
        ptr += 4 * sizeof(float);
        (*denseNormals)[i].normal_x = ndata[0];
        (*denseNormals)[i].normal_y = ndata[1];
        (*denseNormals)[i].normal_z = ndata[2];
        (*denseNormals)[i].curvature = ndata[3];
    }
    
    return Descriptor(keypoints, features, denseCloud, denseNormals);
}

// ============================================================================
// DescriptorExtractor Implementation
// ============================================================================

DescriptorExtractor::DescriptorExtractor(const DescriptorConfig& config) 
    : config_(config) {}

PointCloudPtr DescriptorExtractor::downsample(PointCloudPtr cloud) {
    if (cloud->size() <= 2000) {
        return cloud;  // Small cloud, no downsampling needed
    }
    
    auto downsampled = std::make_shared<PointCloud>();
    
    pcl::VoxelGrid<pcl::PointXYZ> voxelGrid;
    voxelGrid.setInputCloud(cloud);
    voxelGrid.setLeafSize(config_.voxelSize, config_.voxelSize, config_.voxelSize);
    voxelGrid.filter(*downsampled);
    
    // If still too many points, increase voxel size iteratively
    float currentVoxelSize = config_.voxelSize;
    while (downsampled->size() > 20000 && currentVoxelSize < 10.0f) {
        currentVoxelSize *= 1.5f;
        voxelGrid.setLeafSize(currentVoxelSize, currentVoxelSize, currentVoxelSize);
        voxelGrid.filter(*downsampled);
    }
    
    return downsampled;
}

NormalCloudPtr DescriptorExtractor::estimateNormals(PointCloudPtr cloud, float radius) {
    auto normals = std::make_shared<NormalCloud>();
    
    pcl::NormalEstimationOMP<pcl::PointXYZ, pcl::Normal> normalEstimation;
    normalEstimation.setInputCloud(cloud);
    
    auto tree = std::make_shared<pcl::search::KdTree<pcl::PointXYZ>>();
    normalEstimation.setSearchMethod(tree);
    normalEstimation.setRadiusSearch(radius);
    normalEstimation.compute(*normals);
    
    return normals;
}

PointCloudPtr DescriptorExtractor::detectKeypoints(PointCloudPtr cloud, NormalCloudPtr normals) {
    auto keypoints = std::make_shared<PointCloud>();
    
    pcl::ISSKeypoint3D<pcl::PointXYZ, pcl::PointXYZ> issDetector;
    issDetector.setInputCloud(cloud);
    issDetector.setNormals(normals);
    
    auto tree = std::make_shared<pcl::search::KdTree<pcl::PointXYZ>>();
    issDetector.setSearchMethod(tree);
    issDetector.setSalientRadius(config_.issSalientRadius);
    issDetector.setNonMaxRadius(config_.issNonMaxRadius);
    issDetector.setThreshold21(config_.issThreshold21);
    issDetector.setThreshold32(config_.issThreshold32);
    issDetector.setMinNeighbors(config_.issMinNeighbors);
    issDetector.compute(*keypoints);
    
    // If too many keypoints, sort by saliency (surface curvature) and keep top N.
    // Curvature from normals is a proxy for ISS response: high curvature = cusps/pits.
    if (static_cast<int>(keypoints->size()) > config_.maxKeypoints) {
        // Build KD-tree over the downsampled cloud to map keypoints → normals
        pcl::search::KdTree<pcl::PointXYZ> normalTree;
        normalTree.setInputCloud(cloud);
        
        // For each keypoint, find its curvature from the nearest point in the normals cloud
        struct KeypointSaliency {
            pcl::PointXYZ point;
            float curvature;
        };
        std::vector<KeypointSaliency> ranked;
        ranked.reserve(keypoints->size());
        
        std::vector<int> indices(1);
        std::vector<float> distances(1);
        for (size_t i = 0; i < keypoints->size(); ++i) {
            normalTree.nearestKSearch((*keypoints)[i], 1, indices, distances);
            float curv = (*normals)[indices[0]].curvature;
            ranked.push_back({(*keypoints)[i], curv});
        }
        
        // Sort by curvature descending — sharpest features first
        std::sort(ranked.begin(), ranked.end(),
            [](const KeypointSaliency& a, const KeypointSaliency& b) {
                return a.curvature > b.curvature;
            });
        
        // Keep only the top maxKeypoints
        auto sorted = std::make_shared<PointCloud>();
        sorted->reserve(config_.maxKeypoints);
        for (int i = 0; i < config_.maxKeypoints; ++i) {
            sorted->push_back(ranked[i].point);
        }
        keypoints = sorted;
    }
    
    return keypoints;
}

Descriptor DescriptorExtractor::extract(PointCloudPtr cloud) {
    if (!cloud || cloud->empty()) {
        throw std::runtime_error("Cannot extract descriptors from empty point cloud");
    }
    
    // Step 1: Downsample for efficiency (this IS our dense cloud for ICP)
    auto downsampled = downsample(cloud);
    if (downsampled->empty()) {
        throw std::runtime_error("Point cloud is empty after downsampling");
    }
    
    // Step 2: Estimate normals for SHOT features (broad radius = stable orientation)
    auto shotNormals = estimateNormals(downsampled, config_.normalRadius);
    
    // Step 3: Estimate normals for ICP (sharp radius = preserves micro-anatomy)
    auto icpNormals = estimateNormals(downsampled, config_.icpNormalRadius);
    
    // Step 4: Detect ISS keypoints (cusps, pits, fissures, margin lines)
    auto keypoints = detectKeypoints(downsampled, shotNormals);
    if (keypoints->empty()) {
        throw std::runtime_error("No ISS keypoints detected — cloud may be too small or featureless");
    }
    
    // Step 5: Compute SHOT352 descriptors at keypoints
    auto shotFeatures = std::make_shared<SHOTCloud>();
    
    pcl::SHOTEstimationOMP<pcl::PointXYZ, pcl::Normal, pcl::SHOT352> shotEstimation;
    shotEstimation.setInputCloud(keypoints);            // Compute at keypoints
    shotEstimation.setSearchSurface(downsampled);       // Use full cloud as surface
    shotEstimation.setInputNormals(shotNormals);        // SHOT normals (broad, 1.0mm)
    
    auto tree = std::make_shared<pcl::search::KdTree<pcl::PointXYZ>>();
    shotEstimation.setSearchMethod(tree);
    shotEstimation.setRadiusSearch(config_.shotRadius);
    shotEstimation.compute(*shotFeatures);
    
    // Step 6: Filter out NaN descriptors (SHOT produces NaN for degenerate points)
    auto validKeypoints = std::make_shared<PointCloud>();
    auto validFeatures = std::make_shared<SHOTCloud>();
    
    for (size_t i = 0; i < shotFeatures->size(); ++i) {
        // Check if descriptor has any NaN
        bool hasNaN = false;
        for (int d = 0; d < Descriptor::SHOT_DIM; ++d) {
            if (std::isnan((*shotFeatures)[i].descriptor[d])) {
                hasNaN = true;
                break;
            }
        }
        if (!hasNaN) {
            validKeypoints->push_back((*keypoints)[i]);
            validFeatures->push_back((*shotFeatures)[i]);
        }
    }
    
    if (validKeypoints->empty()) {
        throw std::runtime_error("All SHOT descriptors are NaN — adjust radii or check input mesh");
    }
    
    // Step 7: Return descriptor with dense cloud + ICP normals
    // Dense cloud = the VoxelGrid-downsampled cloud (thousands of points)
    // ICP normals = computed at sharp radius (0.5mm) for cusp/fissure sharpness
    return Descriptor(validKeypoints, validFeatures, downsampled, icpNormals);
}

} // namespace aum
