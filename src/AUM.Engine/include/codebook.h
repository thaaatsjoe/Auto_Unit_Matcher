#pragma once

#include "exports.h"
#include "descriptor.h"
#include <vector>
#include <string>
#include <memory>

namespace aum {

/**
 * Bag-of-Words codebook for histogram-based FPFH matching.
 * 
 * The codebook contains K cluster centers (visual words) learned via k-means
 * clustering of FPFH features across many training crowns. Each crown's FPFH
 * features are then assigned to their nearest cluster center, producing a
 * K-dimensional histogram that is robust to partial scans.
 */
class AUM_API Codebook {
public:
    static constexpr int FPFH_DIM = 33;   // FPFH histogram bins
    static constexpr int DEFAULT_K = 128;  // Default number of clusters
    
    Codebook() = default;
    
    /**
     * Get the number of clusters (K).
     */
    int K() const { return k_; }
    
    /**
     * Check if the codebook has been trained/loaded.
     */
    bool isReady() const { return k_ > 0 && !centers_.empty(); }
    
    /**
     * Train the codebook using k-means on a collection of FPFH feature clouds.
     * @param featureSets Vector of FPFH feature clouds from training crowns
     * @param k Number of clusters (default 128)
     * @param maxIter Maximum k-means iterations (default 50)
     * @param sampleLimit Max features to sample per crown for training (default 500)
     */
    void train(const std::vector<FPFHCloudPtr>& featureSets, 
               int k = DEFAULT_K, 
               int maxIter = 50,
               int sampleLimit = 500);
    
    /**
     * Compute a histogram vector from a descriptor using this codebook.
     * For each FPFH point in the descriptor, finds the nearest cluster center
     * and increments that bin. Returns an L2-normalized K-dimensional vector.
     * @param desc Input descriptor with raw FPFH features
     * @return K-dimensional histogram vector (L2-normalized)
     */
    std::vector<float> computeHistogram(const Descriptor& desc) const;
    
    /**
     * Save codebook to binary file.
     * Format: [K:4 bytes][centers: K * 33 * sizeof(float)]
     */
    void save(const std::string& path) const;
    
    /**
     * Load codebook from binary file.
     */
    static Codebook load(const std::string& path);

private:
    int k_ = 0;
    
    // Cluster centers: k_ rows x FPFH_DIM columns, stored flat
    // centers_[i * FPFH_DIM + d] = center i, dimension d
    std::vector<float> centers_;
    
    /**
     * Find nearest cluster center for a single FPFH histogram.
     * @param histogram 33-float FPFH histogram
     * @return Index of nearest cluster (0 to K-1)
     */
    int findNearestCluster(const float* histogram) const;
};

} // namespace aum
