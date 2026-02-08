// FPFH Descriptor Extraction Implementation
// Phase 1 will refine parameters and optimization

#include "descriptor.h"
#include <pcl/filters/voxel_grid.h>
#include <pcl/features/normal_3d.h>
#include <pcl/features/fpfh.h>
#include <stdexcept>
#include <cstring>

namespace aum {

Descriptor::Descriptor(FPFHCloudPtr features) : features_(features) {}

std::vector<float> Descriptor::getAggregatedVector() const {
    if (!features_ || features_->empty()) {
        return std::vector<float>(33, 0.0f);
    }
    
    // Aggregate all FPFH histograms by averaging
    std::vector<float> aggregated(33, 0.0f);
    
    for (const auto& point : features_->points) {
        for (int i = 0; i < 33; ++i) {
            aggregated[i] += point.histogram[i];
        }
    }
    
    // Normalize
    float sum = 0.0f;
    for (float v : aggregated) sum += v;
    if (sum > 0) {
        for (float& v : aggregated) v /= sum;
    }
    
    return aggregated;
}

std::vector<uint8_t> Descriptor::serialize() const {
    if (!features_) {
        return {};
    }
    
    // Format: [num_points (4 bytes)] [histogram data (33 floats per point)]
    size_t numPoints = features_->size();
    size_t dataSize = sizeof(uint32_t) + numPoints * 33 * sizeof(float);
    
    std::vector<uint8_t> blob(dataSize);
    uint8_t* ptr = blob.data();
    
    // Write point count
    uint32_t count = static_cast<uint32_t>(numPoints);
    std::memcpy(ptr, &count, sizeof(count));
    ptr += sizeof(count);
    
    // Write histogram data
    for (const auto& point : features_->points) {
        std::memcpy(ptr, point.histogram, 33 * sizeof(float));
        ptr += 33 * sizeof(float);
    }
    
    return blob;
}

Descriptor Descriptor::deserialize(const uint8_t* data, size_t len) {
    if (!data || len < sizeof(uint32_t)) {
        throw std::runtime_error("Invalid descriptor blob");
    }
    
    const uint8_t* ptr = data;
    
    // Read point count
    uint32_t numPoints;
    std::memcpy(&numPoints, ptr, sizeof(numPoints));
    ptr += sizeof(numPoints);
    
    // Validate size
    size_t expectedSize = sizeof(uint32_t) + numPoints * 33 * sizeof(float);
    if (len != expectedSize) {
        throw std::runtime_error("Descriptor blob size mismatch");
    }
    
    // Read histogram data
    auto features = std::make_shared<FPFHCloud>();
    features->points.resize(numPoints);
    
    for (size_t i = 0; i < numPoints; ++i) {
        std::memcpy(features->points[i].histogram, ptr, 33 * sizeof(float));
        ptr += 33 * sizeof(float);
    }
    
    features->width = numPoints;
    features->height = 1;
    
    return Descriptor(features);
}

// DescriptorExtractor implementation

DescriptorExtractor::DescriptorExtractor(const DescriptorConfig& config) : config_(config) {}

PointCloudPtr DescriptorExtractor::downsample(PointCloudPtr cloud) {
    pcl::VoxelGrid<pcl::PointXYZ> voxelGrid;
    voxelGrid.setInputCloud(cloud);
    voxelGrid.setLeafSize(config_.voxelSize, config_.voxelSize, config_.voxelSize);
    
    auto downsampled = std::make_shared<PointCloud>();
    voxelGrid.filter(*downsampled);
    
    return downsampled;
}

pcl::PointCloud<pcl::Normal>::Ptr DescriptorExtractor::estimateNormals(PointCloudPtr cloud) {
    pcl::NormalEstimation<pcl::PointXYZ, pcl::Normal> normalEstimation;
    normalEstimation.setInputCloud(cloud);
    
    pcl::search::KdTree<pcl::PointXYZ>::Ptr tree(new pcl::search::KdTree<pcl::PointXYZ>());
    normalEstimation.setSearchMethod(tree);
    normalEstimation.setRadiusSearch(config_.normalRadius);
    
    auto normals = std::make_shared<pcl::PointCloud<pcl::Normal>>();
    normalEstimation.compute(*normals);
    
    return normals;
}

Descriptor DescriptorExtractor::extract(PointCloudPtr cloud) {
    if (!cloud || cloud->empty()) {
        throw std::runtime_error("Empty point cloud");
    }
    
    // 1. Downsample
    auto downsampled = downsample(cloud);
    
    // 2. Estimate normals
    auto normals = estimateNormals(downsampled);
    
    // 3. Compute FPFH
    pcl::FPFHEstimation<pcl::PointXYZ, pcl::Normal, FPFHSignature> fpfh;
    fpfh.setInputCloud(downsampled);
    fpfh.setInputNormals(normals);
    
    pcl::search::KdTree<pcl::PointXYZ>::Ptr tree(new pcl::search::KdTree<pcl::PointXYZ>());
    fpfh.setSearchMethod(tree);
    fpfh.setRadiusSearch(config_.fpfhRadius);
    
    auto features = std::make_shared<FPFHCloud>();
    fpfh.compute(*features);
    
    return Descriptor(features);
}

} // namespace aum
