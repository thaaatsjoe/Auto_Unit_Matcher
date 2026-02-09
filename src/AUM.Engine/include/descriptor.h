#pragma once

#include "exports.h"
#include <pcl/point_types.h>
#include <pcl/point_cloud.h>
#include <pcl/features/fpfh.h>
#include <vector>
#include <memory>

namespace aum {

using PointCloud = pcl::PointCloud<pcl::PointXYZ>;
using PointCloudPtr = pcl::PointCloud<pcl::PointXYZ>::Ptr;
using FPFHSignature = pcl::FPFHSignature33;
using FPFHCloud = pcl::PointCloud<FPFHSignature>;
using FPFHCloudPtr = pcl::PointCloud<FPFHSignature>::Ptr;

/**
 * Descriptor extraction configuration.
 */
struct AUM_API DescriptorConfig {
    float voxelSize = 0.5f;           // Downsampling voxel size (mm)
    float normalRadius = 2.0f;        // Normal estimation radius (mm)
    float fpfhRadius = 5.0f;          // FPFH search radius (mm)
    int numPoints = 1000;             // Target number of keypoints
};

/**
 * Container for extracted descriptors.
 */
class AUM_API Descriptor {
public:
    Descriptor() = default;
    explicit Descriptor(FPFHCloudPtr features);
    
    /**
     * Get the raw FPFH features.
     */
    FPFHCloudPtr getFeatures() const { return features_; }
    
    /**
     * Get aggregated descriptor vector for FAISS.
     * Returns a single vector representing the entire unit.
     */
    std::vector<float> getAggregatedVector() const;
    
    /**
     * Serialize to binary blob.
     */
    std::vector<uint8_t> serialize() const;
    
    /**
     * Deserialize from binary blob.
     */
    static Descriptor deserialize(const uint8_t* data, size_t len);
    
    /**
     * Get descriptor dimension for FAISS.
     */
    static constexpr int dimension() { return 33; } // FPFH has 33 bins

private:
    FPFHCloudPtr features_;
};

/**
 * FPFH descriptor extractor.
 */
class AUM_API DescriptorExtractor {
public:
    explicit DescriptorExtractor(const DescriptorConfig& config = {});
    
    /**
     * Extract FPFH descriptors from a point cloud.
     * @param cloud Input point cloud
     * @return Descriptor object
     */
    Descriptor extract(PointCloudPtr cloud);

private:
    DescriptorConfig config_;
    
    PointCloudPtr downsample(PointCloudPtr cloud);
    pcl::PointCloud<pcl::Normal>::Ptr estimateNormals(PointCloudPtr cloud);
};

} // namespace aum
