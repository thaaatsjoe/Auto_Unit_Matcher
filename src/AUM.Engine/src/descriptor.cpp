// FPFH Descriptor Extraction Implementation
// Extracts Fast Point Feature Histograms from point clouds

#include "descriptor.h"
#include <pcl/features/fpfh_omp.h>
#include <pcl/features/normal_3d_omp.h>
#include <pcl/filters/voxel_grid.h>
#include <pcl/search/kdtree.h>
#include <stdexcept>
#include <cstring>
#include <numeric>

namespace aum {

// ============================================================================
// Descriptor Implementation
// ============================================================================

Descriptor::Descriptor(FPFHCloudPtr features) : features_(features) {}

std::vector<float> Descriptor::getAggregatedVector() const {
    if (!features_ || features_->empty()) {
        return std::vector<float>(33, 0.0f);
    }
    
    // Aggregate all FPFH histograms by averaging
    std::vector<float> aggregated(33, 0.0f);
    
    for (const auto& fpfh : *features_) {
        for (int i = 0; i < 33; ++i) {
            aggregated[i] += fpfh.histogram[i];
        }
    }
    
    // Normalize
    float invCount = 1.0f / static_cast<float>(features_->size());
    for (float& val : aggregated) {
        val *= invCount;
    }
    
    // L2 normalize for better matching
    float norm = 0.0f;
    for (float val : aggregated) {
        norm += val * val;
    }
    if (norm > 0.0f) {
        norm = std::sqrt(norm);
        for (float& val : aggregated) {
            val /= norm;
        }
    }
    
    return aggregated;
}

std::vector<uint8_t> Descriptor::serialize() const {
    if (!features_ || features_->empty()) {
        return {};
    }
    
    // Format: [count:4 bytes][features:count * 33 * 4 bytes]
    size_t count = features_->size();
    size_t dataSize = 4 + count * 33 * sizeof(float);
    
    std::vector<uint8_t> blob(dataSize);
    
    // Write count
    uint32_t countU32 = static_cast<uint32_t>(count);
    std::memcpy(blob.data(), &countU32, 4);
    
    // Write feature data
    float* featureData = reinterpret_cast<float*>(blob.data() + 4);
    for (size_t i = 0; i < count; ++i) {
        std::memcpy(featureData + i * 33, (*features_)[i].histogram, 33 * sizeof(float));
    }
    
    return blob;
}

Descriptor Descriptor::deserialize(const uint8_t* data, size_t len) {
    if (len < 4) {
        throw std::runtime_error("Invalid descriptor blob: too short");
    }
    
    uint32_t count;
    std::memcpy(&count, data, 4);
    
    size_t expectedSize = 4 + count * 33 * sizeof(float);
    if (len < expectedSize) {
        throw std::runtime_error("Invalid descriptor blob: size mismatch");
    }
    
    auto features = std::make_shared<FPFHCloud>();
    features->resize(count);
    
    const float* featureData = reinterpret_cast<const float*>(data + 4);
    for (size_t i = 0; i < count; ++i) {
        std::memcpy((*features)[i].histogram, featureData + i * 33, 33 * sizeof(float));
    }
    
    return Descriptor(features);
}

// ============================================================================
// DescriptorExtractor Implementation
// ============================================================================

DescriptorExtractor::DescriptorExtractor(const DescriptorConfig& config) 
    : config_(config) {}

PointCloudPtr DescriptorExtractor::downsample(PointCloudPtr cloud) {
    if (cloud->size() <= static_cast<size_t>(config_.numPoints)) {
        return cloud;  // No need to downsample
    }
    
    auto downsampled = std::make_shared<PointCloud>();
    
    pcl::VoxelGrid<pcl::PointXYZ> voxelGrid;
    voxelGrid.setInputCloud(cloud);
    voxelGrid.setLeafSize(config_.voxelSize, config_.voxelSize, config_.voxelSize);
    voxelGrid.filter(*downsampled);
    
    // If still too many points, increase voxel size iteratively
    float currentVoxelSize = config_.voxelSize;
    while (downsampled->size() > static_cast<size_t>(config_.numPoints) * 2 && currentVoxelSize < 10.0f) {
        currentVoxelSize *= 1.5f;
        voxelGrid.setLeafSize(currentVoxelSize, currentVoxelSize, currentVoxelSize);
        voxelGrid.filter(*downsampled);
    }
    
    return downsampled;
}

pcl::PointCloud<pcl::Normal>::Ptr DescriptorExtractor::estimateNormals(PointCloudPtr cloud) {
    auto normals = std::make_shared<pcl::PointCloud<pcl::Normal>>();
    
    pcl::NormalEstimationOMP<pcl::PointXYZ, pcl::Normal> normalEstimation;
    normalEstimation.setInputCloud(cloud);
    
    auto tree = std::make_shared<pcl::search::KdTree<pcl::PointXYZ>>();
    normalEstimation.setSearchMethod(tree);
    normalEstimation.setRadiusSearch(config_.normalRadius);
    normalEstimation.compute(*normals);
    
    return normals;
}

Descriptor DescriptorExtractor::extract(PointCloudPtr cloud) {
    if (!cloud || cloud->empty()) {
        throw std::runtime_error("Cannot extract descriptors from empty point cloud");
    }
    
    // Step 1: Downsample for efficiency
    auto downsampled = downsample(cloud);
    
    if (downsampled->empty()) {
        throw std::runtime_error("Point cloud is empty after downsampling");
    }
    
    // Step 2: Estimate normals
    auto normals = estimateNormals(downsampled);
    
    // Step 3: Compute FPFH features
    auto features = std::make_shared<FPFHCloud>();
    
    pcl::FPFHEstimationOMP<pcl::PointXYZ, pcl::Normal, pcl::FPFHSignature33> fpfh;
    fpfh.setInputCloud(downsampled);
    fpfh.setInputNormals(normals);
    
    auto tree = std::make_shared<pcl::search::KdTree<pcl::PointXYZ>>();
    fpfh.setSearchMethod(tree);
    fpfh.setRadiusSearch(config_.fpfhRadius);
    fpfh.compute(*features);
    
    return Descriptor(features);
}

} // namespace aum
