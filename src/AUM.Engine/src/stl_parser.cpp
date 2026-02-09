// STL Parser Implementation
// Supports both ASCII and binary STL formats

#include "stl_parser.h"
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <cstring>
#include <algorithm>
#include <cctype>

namespace aum {

// Binary STL format:
// 80 bytes header
// 4 bytes triangle count (uint32)
// For each triangle:
//   12 bytes normal (3 floats)
//   36 bytes vertices (9 floats - 3 vertices * 3 coords)
//   2 bytes attribute (unused)

struct BinaryTriangle {
    float normal[3];
    float v1[3];
    float v2[3];
    float v3[3];
    uint16_t attribute;
};

bool STLParser::isBinarySTL(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        return false;
    }
    
    // Read header
    char header[80];
    file.read(header, 80);
    if (!file.good()) {
        return false;
    }
    
    // Check if header starts with "solid" - could be ASCII
    // But some binary files also start with "solid", so verify with triangle count
    std::string headerStr(header, std::min<size_t>(80, strlen(header)));
    bool startsWithSolid = headerStr.substr(0, 5) == "solid";
    
    // Read triangle count
    uint32_t triangleCount;
    file.read(reinterpret_cast<char*>(&triangleCount), sizeof(triangleCount));
    if (!file.good()) {
        return !startsWithSolid;  // If we can't read count, assume ASCII if it starts with solid
    }
    
    // Check if file size matches expected binary size
    file.seekg(0, std::ios::end);
    std::streampos fileSize = file.tellg();
    
    // Expected size: 80 (header) + 4 (count) + n * 50 (triangles)
    size_t expectedSize = 80 + 4 + static_cast<size_t>(triangleCount) * 50;
    
    // If file size matches expected binary size, it's binary
    return static_cast<size_t>(fileSize) == expectedSize;
}

PointCloudPtr STLParser::loadBinarySTL(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file.is_open()) {
        throw std::runtime_error("Cannot open STL file: " + path);
    }
    
    // Skip header
    file.seekg(80);
    
    // Read triangle count
    uint32_t triangleCount;
    file.read(reinterpret_cast<char*>(&triangleCount), sizeof(triangleCount));
    
    if (!file.good() || triangleCount == 0) {
        throw std::runtime_error("Invalid binary STL file: " + path);
    }
    
    // Create point cloud - 3 vertices per triangle
    auto cloud = std::make_shared<PointCloud>();
    cloud->reserve(triangleCount * 3);
    
    // Read triangles
    for (uint32_t i = 0; i < triangleCount; ++i) {
        BinaryTriangle tri;
        
        // Read normal (skip it, we'll recompute if needed)
        file.read(reinterpret_cast<char*>(tri.normal), 12);
        
        // Read 3 vertices
        file.read(reinterpret_cast<char*>(tri.v1), 12);
        file.read(reinterpret_cast<char*>(tri.v2), 12);
        file.read(reinterpret_cast<char*>(tri.v3), 12);
        
        // Read attribute (unused)
        file.read(reinterpret_cast<char*>(&tri.attribute), 2);
        
        if (!file.good()) {
            throw std::runtime_error("Error reading binary STL at triangle " + std::to_string(i));
        }
        
        // Add vertices to point cloud
        pcl::PointXYZ p1, p2, p3;
        p1.x = tri.v1[0]; p1.y = tri.v1[1]; p1.z = tri.v1[2];
        p2.x = tri.v2[0]; p2.y = tri.v2[1]; p2.z = tri.v2[2];
        p3.x = tri.v3[0]; p3.y = tri.v3[1]; p3.z = tri.v3[2];
        
        cloud->push_back(p1);
        cloud->push_back(p2);
        cloud->push_back(p3);
    }
    
    cloud->width = static_cast<uint32_t>(cloud->size());
    cloud->height = 1;
    cloud->is_dense = true;
    
    return cloud;
}

PointCloudPtr STLParser::loadASCIISTL(const std::string& path) {
    std::ifstream file(path);
    if (!file.is_open()) {
        throw std::runtime_error("Cannot open STL file: " + path);
    }
    
    auto cloud = std::make_shared<PointCloud>();
    std::string line;
    
    // Skip "solid name" line
    std::getline(file, line);
    
    while (std::getline(file, line)) {
        // Trim whitespace
        size_t start = line.find_first_not_of(" \t");
        if (start == std::string::npos) continue;
        line = line.substr(start);
        
        // Check for vertex lines
        if (line.substr(0, 6) == "vertex") {
            float x, y, z;
            if (sscanf(line.c_str(), "vertex %f %f %f", &x, &y, &z) == 3) {
                pcl::PointXYZ pt;
                pt.x = x;
                pt.y = y;
                pt.z = z;
                cloud->push_back(pt);
            }
        }
        // Stop at endsolid
        else if (line.substr(0, 8) == "endsolid") {
            break;
        }
    }
    
    if (cloud->empty()) {
        throw std::runtime_error("No vertices found in ASCII STL: " + path);
    }
    
    cloud->width = static_cast<uint32_t>(cloud->size());
    cloud->height = 1;
    cloud->is_dense = true;
    
    return cloud;
}

PointCloudPtr STLParser::load(const std::string& path) {
    if (!isValidSTL(path)) {
        throw std::runtime_error("File does not exist or is not a valid STL: " + path);
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
    
    // Check file size (minimum valid STL is 84 bytes for binary with 0 triangles)
    file.seekg(0, std::ios::end);
    std::streampos size = file.tellg();
    if (size < 84) {
        // Could be a tiny ASCII STL, check for "solid" keyword
        file.seekg(0);
        char buffer[6] = {0};
        file.read(buffer, 5);
        return std::string(buffer) == "solid";
    }
    
    return true;
}

} // namespace aum
