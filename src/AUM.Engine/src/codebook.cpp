// Codebook Implementation - Bag-of-Words via K-Means Clustering
// Produces histogram vectors robust to partial scans

#include "codebook.h"
#include <stdexcept>
#include <cstring>
#include <cmath>
#include <algorithm>
#include <numeric>
#include <random>
#include <fstream>
#include <limits>

namespace aum {

void Codebook::train(const std::vector<FPFHCloudPtr>& featureSets,
                     int k, int maxIter, int sampleLimit) {
    if (featureSets.empty()) {
        throw std::runtime_error("Cannot train codebook: no feature sets provided");
    }
    if (k <= 0) {
        throw std::runtime_error("K must be positive");
    }
    
    // ================================================================
    // Step 1: Collect training features (sample from each crown)
    // ================================================================
    std::vector<std::vector<float>> allFeatures;
    allFeatures.reserve(featureSets.size() * sampleLimit);
    
    std::mt19937 rng(42); // Fixed seed for reproducibility
    
    for (const auto& features : featureSets) {
        if (!features || features->empty()) continue;
        
        size_t n = features->size();
        
        if (static_cast<int>(n) <= sampleLimit) {
            // Take all features from this crown
            for (size_t i = 0; i < n; ++i) {
                std::vector<float> feat(FPFH_DIM);
                std::memcpy(feat.data(), (*features)[i].histogram, FPFH_DIM * sizeof(float));
                allFeatures.push_back(std::move(feat));
            }
        } else {
            // Randomly sample sampleLimit features
            std::vector<size_t> indices(n);
            std::iota(indices.begin(), indices.end(), 0);
            std::shuffle(indices.begin(), indices.end(), rng);
            
            for (int s = 0; s < sampleLimit; ++s) {
                std::vector<float> feat(FPFH_DIM);
                std::memcpy(feat.data(), (*features)[indices[s]].histogram, FPFH_DIM * sizeof(float));
                allFeatures.push_back(std::move(feat));
            }
        }
    }
    
    if (allFeatures.size() < static_cast<size_t>(k)) {
        throw std::runtime_error("Not enough features to train " + std::to_string(k) + 
                                 " clusters (have " + std::to_string(allFeatures.size()) + ")");
    }
    
    // ================================================================
    // Step 2: K-Means++ initialization
    // ================================================================
    k_ = k;
    centers_.resize(k_ * FPFH_DIM, 0.0f);
    
    // Pick first center randomly
    std::uniform_int_distribution<size_t> dist(0, allFeatures.size() - 1);
    size_t firstIdx = dist(rng);
    std::memcpy(centers_.data(), allFeatures[firstIdx].data(), FPFH_DIM * sizeof(float));
    
    // Pick remaining centers using k-means++ (probability proportional to distance squared)
    std::vector<float> minDistSq(allFeatures.size(), std::numeric_limits<float>::max());
    
    for (int c = 1; c < k_; ++c) {
        // Update minimum distances to nearest existing center
        for (size_t i = 0; i < allFeatures.size(); ++i) {
            float d = 0.0f;
            const float* center = &centers_[(c - 1) * FPFH_DIM];
            for (int d_idx = 0; d_idx < FPFH_DIM; ++d_idx) {
                float diff = allFeatures[i][d_idx] - center[d_idx];
                d += diff * diff;
            }
            minDistSq[i] = std::min(minDistSq[i], d);
        }
        
        // Sample next center with probability proportional to D^2
        std::discrete_distribution<size_t> weightedDist(minDistSq.begin(), minDistSq.end());
        size_t nextIdx = weightedDist(rng);
        std::memcpy(&centers_[c * FPFH_DIM], allFeatures[nextIdx].data(), FPFH_DIM * sizeof(float));
    }
    
    // ================================================================
    // Step 3: K-Means iterations
    // ================================================================
    std::vector<int> assignments(allFeatures.size());
    std::vector<int> clusterCounts(k_, 0);
    
    for (int iter = 0; iter < maxIter; ++iter) {
        // Assign each feature to nearest center
        int changedCount = 0;
        for (size_t i = 0; i < allFeatures.size(); ++i) {
            int nearest = findNearestCluster(allFeatures[i].data());
            if (nearest != assignments[i]) {
                ++changedCount;
            }
            assignments[i] = nearest;
        }
        
        // Check for convergence
        if (iter > 0 && changedCount == 0) {
            break;
        }
        
        // Recompute centers
        std::fill(centers_.begin(), centers_.end(), 0.0f);
        std::fill(clusterCounts.begin(), clusterCounts.end(), 0);
        
        for (size_t i = 0; i < allFeatures.size(); ++i) {
            int c = assignments[i];
            clusterCounts[c]++;
            for (int d = 0; d < FPFH_DIM; ++d) {
                centers_[c * FPFH_DIM + d] += allFeatures[i][d];
            }
        }
        
        // Normalize (divide by count)
        for (int c = 0; c < k_; ++c) {
            if (clusterCounts[c] > 0) {
                float inv = 1.0f / static_cast<float>(clusterCounts[c]);
                for (int d = 0; d < FPFH_DIM; ++d) {
                    centers_[c * FPFH_DIM + d] *= inv;
                }
            }
        }
    }
}

std::vector<float> Codebook::computeHistogram(const Descriptor& desc) const {
    if (!isReady()) {
        throw std::runtime_error("Codebook not trained/loaded");
    }
    
    auto features = desc.getFeatures();
    if (!features || features->empty()) {
        return std::vector<float>(k_, 0.0f);
    }
    
    // Build histogram by assigning each FPFH point to nearest cluster
    std::vector<float> histogram(k_, 0.0f);
    
    for (size_t i = 0; i < features->size(); ++i) {
        int cluster = findNearestCluster((*features)[i].histogram);
        histogram[cluster] += 1.0f;
    }
    
    // L2 normalize the histogram
    float norm = 0.0f;
    for (float val : histogram) {
        norm += val * val;
    }
    if (norm > 0.0f) {
        norm = std::sqrt(norm);
        for (float& val : histogram) {
            val /= norm;
        }
    }
    
    return histogram;
}

int Codebook::findNearestCluster(const float* histogram) const {
    int bestCluster = 0;
    float bestDist = std::numeric_limits<float>::max();
    
    for (int c = 0; c < k_; ++c) {
        float dist = 0.0f;
        const float* center = &centers_[c * FPFH_DIM];
        for (int d = 0; d < FPFH_DIM; ++d) {
            float diff = histogram[d] - center[d];
            dist += diff * diff;
        }
        if (dist < bestDist) {
            bestDist = dist;
            bestCluster = c;
        }
    }
    
    return bestCluster;
}

void Codebook::save(const std::string& path) const {
    if (!isReady()) {
        throw std::runtime_error("Cannot save: codebook not trained");
    }
    
    std::ofstream file(path, std::ios::binary);
    if (!file) {
        throw std::runtime_error("Cannot open file for writing: " + path);
    }
    
    // Write K
    uint32_t kU32 = static_cast<uint32_t>(k_);
    file.write(reinterpret_cast<const char*>(&kU32), sizeof(kU32));
    
    // Write cluster centers
    file.write(reinterpret_cast<const char*>(centers_.data()),
               centers_.size() * sizeof(float));
    
    if (!file) {
        throw std::runtime_error("Error writing codebook to: " + path);
    }
}

Codebook Codebook::load(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) {
        throw std::runtime_error("Cannot open codebook file: " + path);
    }
    
    Codebook cb;
    
    // Read K
    uint32_t kU32;
    file.read(reinterpret_cast<char*>(&kU32), sizeof(kU32));
    cb.k_ = static_cast<int>(kU32);
    
    if (cb.k_ <= 0 || cb.k_ > 10000) {
        throw std::runtime_error("Invalid codebook K value: " + std::to_string(cb.k_));
    }
    
    // Read cluster centers
    cb.centers_.resize(cb.k_ * FPFH_DIM);
    file.read(reinterpret_cast<char*>(cb.centers_.data()),
              cb.centers_.size() * sizeof(float));
    
    if (!file) {
        throw std::runtime_error("Error reading codebook from: " + path);
    }
    
    return cb;
}

} // namespace aum
