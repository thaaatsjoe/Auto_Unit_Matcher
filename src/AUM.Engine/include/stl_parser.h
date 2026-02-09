#pragma once

#include "exports.h"
#include <pcl/point_types.h>
#include <pcl/point_cloud.h>
#include <string>

namespace aum {

using PointCloud = pcl::PointCloud<pcl::PointXYZ>;
using PointCloudPtr = pcl::PointCloud<pcl::PointXYZ>::Ptr;

/**
 * Parse an STL file and return a point cloud.
 * Supports both ASCII and binary STL formats.
 */
class AUM_API STLParser {
public:
    /**
     * Load an STL file into a point cloud.
     * @param path Path to the STL file
     * @return Point cloud containing all vertices
     * @throws std::runtime_error if file cannot be read or parsed
     */
    static PointCloudPtr load(const std::string& path);
    
    /**
     * Check if a file is a valid STL file.
     */
    static bool isValidSTL(const std::string& path);

private:
    static PointCloudPtr loadBinarySTL(const std::string& path);
    static PointCloudPtr loadASCIISTL(const std::string& path);
    static bool isBinarySTL(const std::string& path);
};

} // namespace aum
