// STL Parser Implementation
// Phase 1 will add full implementation

#include "stl_parser.h"
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <cstring>

namespace aum {

PointCloudPtr STLParser::load(const std::string& path) {
    if (!isValidSTL(path)) {
        throw std::runtime_error("Invalid or missing STL file: " + path);
    }
    
    if (isBinarySTL(path)) {
        return loadBinarySTL(path);
    } else {
        return loadASCIISTL(path);
    }
}

bool STLParser::isValidSTL(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        return false;
    }
    
    // Check file size (binary STL has 84-byte header + triangles)
    file.seekg(0, std::ios::end);
    auto size = file.tellg();
    return size > 84;
}

bool STLParser::isBinarySTL(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        return false;
    }
    
    // Read first 80 bytes (header) and check for "solid" keyword
    char header[80];
    file.read(header, 80);
    
    // ASCII STL typically starts with "solid"
    // But binary can too, so check if the triangle count makes sense
    uint32_t triangleCount;
    file.read(reinterpret_cast<char*>(&triangleCount), sizeof(triangleCount));
    
    // Binary STL: 84 bytes header + 50 bytes per triangle
    file.seekg(0, std::ios::end);
    auto fileSize = file.tellg();
    auto expectedSize = 84 + triangleCount * 50;
    
    return std::abs(static_cast<long>(fileSize) - static_cast<long>(expectedSize)) < 10;
}

PointCloudPtr STLParser::loadBinarySTL(const std::string& path) {
    // TODO: Phase 1 - Full implementation
    // For now, return an empty point cloud
    auto cloud = std::make_shared<PointCloud>();
    
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        throw std::runtime_error("Cannot open file: " + path);
    }
    
    // Skip 80-byte header
    file.seekg(80, std::ios::beg);
    
    // Read triangle count
    uint32_t triangleCount;
    file.read(reinterpret_cast<char*>(&triangleCount), sizeof(triangleCount));
    
    // Reserve space (3 vertices per triangle)
    cloud->points.reserve(triangleCount * 3);
    
    // Read triangles
    for (uint32_t i = 0; i < triangleCount; ++i) {
        // Skip normal (12 bytes)
        file.seekg(12, std::ios::cur);
        
        // Read 3 vertices
        for (int v = 0; v < 3; ++v) {
            float x, y, z;
            file.read(reinterpret_cast<char*>(&x), sizeof(float));
            file.read(reinterpret_cast<char*>(&y), sizeof(float));
            file.read(reinterpret_cast<char*>(&z), sizeof(float));
            cloud->points.emplace_back(x, y, z);
        }
        
        // Skip attribute byte count (2 bytes)
        file.seekg(2, std::ios::cur);
    }
    
    cloud->width = static_cast<uint32_t>(cloud->points.size());
    cloud->height = 1;
    cloud->is_dense = true;
    
    return cloud;
}

PointCloudPtr STLParser::loadASCIISTL(const std::string& path) {
    // TODO: Phase 1 - Full implementation
    auto cloud = std::make_shared<PointCloud>();
    
    std::ifstream file(path);
    if (!file.is_open()) {
        throw std::runtime_error("Cannot open file: " + path);
    }
    
    std::string line;
    while (std::getline(file, line)) {
        std::istringstream iss(line);
        std::string keyword;
        iss >> keyword;
        
        if (keyword == "vertex") {
            float x, y, z;
            iss >> x >> y >> z;
            cloud->points.emplace_back(x, y, z);
        }
    }
    
    cloud->width = static_cast<uint32_t>(cloud->points.size());
    cloud->height = 1;
    cloud->is_dense = true;
    
    return cloud;
}

} // namespace aum
