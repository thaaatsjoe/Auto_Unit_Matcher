// Matching Index Unit Tests
// Tests for FAISS-based similarity search with codebook histograms

#include <gtest/gtest.h>
#include "matching.h"
#include "codebook.h"
#include "descriptor.h"
#include <pcl/point_cloud.h>
#include <pcl/point_types.h>
#include <cmath>

namespace aum {
namespace test {

class MatchingTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Create test point clouds that are similar but not identical
        cloud1_ = createSphereCloud(1.0f, 0.0f, 0.0f, 0.0f);
        cloud2_ = createSphereCloud(1.0f, 0.1f, 0.0f, 0.0f);  // Similar, slightly offset
        cloud3_ = createCubeCloud(2.0f, 5.0f, 0.0f, 0.0f);   // Different shape
        
        // Extract features for codebook training
        DescriptorExtractor extractor;
        auto desc1 = extractor.extract(cloud1_);
        auto desc2 = extractor.extract(cloud2_);
        auto desc3 = extractor.extract(cloud3_);
        
        // Train a small test codebook from the test clouds
        std::vector<FPFHCloudPtr> trainingFeatures;
        trainingFeatures.push_back(desc1.getFeatures());
        trainingFeatures.push_back(desc2.getFeatures());
        trainingFeatures.push_back(desc3.getFeatures());
        
        codebook_ = std::make_unique<Codebook>();
        codebook_->train(trainingFeatures, 16);  // Small K for tests
    }
    
    PointCloudPtr createSphereCloud(float radius, float cx, float cy, float cz) {
        auto cloud = std::make_shared<PointCloud>();
        
        // Create points on a sphere surface
        for (int lat = 0; lat < 20; ++lat) {
            for (int lon = 0; lon < 40; ++lon) {
                float theta = static_cast<float>(lat) / 20.0f * 3.14159f;
                float phi = static_cast<float>(lon) / 40.0f * 6.28318f;
                
                pcl::PointXYZ pt;
                pt.x = cx + radius * std::sin(theta) * std::cos(phi);
                pt.y = cy + radius * std::sin(theta) * std::sin(phi);
                pt.z = cz + radius * std::cos(theta);
                cloud->push_back(pt);
            }
        }
        
        cloud->width = static_cast<uint32_t>(cloud->size());
        cloud->height = 1;
        cloud->is_dense = true;
        return cloud;
    }
    
    PointCloudPtr createCubeCloud(float size, float cx, float cy, float cz) {
        auto cloud = std::make_shared<PointCloud>();
        
        // Create points on cube faces
        float step = size / 10.0f;
        for (float x = -size/2; x <= size/2; x += step) {
            for (float y = -size/2; y <= size/2; y += step) {
                cloud->push_back(pcl::PointXYZ(cx + x, cy + y, cz - size/2));
                cloud->push_back(pcl::PointXYZ(cx + x, cy + y, cz + size/2));
            }
        }
        
        cloud->width = static_cast<uint32_t>(cloud->size());
        cloud->height = 1;
        cloud->is_dense = true;
        return cloud;
    }
    
    PointCloudPtr cloud1_;
    PointCloudPtr cloud2_; // Similar to cloud1_
    PointCloudPtr cloud3_; // Different shape
    std::unique_ptr<Codebook> codebook_;
};

TEST_F(MatchingTest, CreateIndex) {
    MatchingIndex index(codebook_.get());
    EXPECT_EQ(index.size(), 0);
}

TEST_F(MatchingTest, AddToIndex) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(cloud1_);
    
    MatchingIndex index(codebook_.get());
    index.add(desc, 100);
    
    EXPECT_EQ(index.size(), 1);
}

TEST_F(MatchingTest, QueryEmptyIndex) {
    DescriptorExtractor extractor;
    Descriptor query = extractor.extract(cloud1_);
    
    MatchingIndex index(codebook_.get());
    auto results = index.query(query, 5);
    
    EXPECT_TRUE(results.empty());
}

TEST_F(MatchingTest, QueryReturnsResults) {
    DescriptorExtractor extractor;
    Descriptor desc1 = extractor.extract(cloud1_);
    Descriptor query = extractor.extract(cloud2_);
    
    MatchingIndex index(codebook_.get());
    index.add(desc1, 100);
    
    auto results = index.query(query, 5);
    
    EXPECT_EQ(results.size(), 1);
    EXPECT_EQ(results[0].id, 100);
}

TEST_F(MatchingTest, SimilarShapesHaveHigherConfidence) {
    DescriptorExtractor extractor;
    Descriptor desc1 = extractor.extract(cloud1_);
    Descriptor desc3 = extractor.extract(cloud3_);
    Descriptor query = extractor.extract(cloud2_);  // Similar to cloud1_
    
    MatchingIndex index(codebook_.get());
    index.add(desc1, 1);  // Sphere
    index.add(desc3, 3);  // Cube
    
    auto results = index.query(query, 5);  // Query with similar sphere
    
    ASSERT_GE(results.size(), 2);
    
    // Find results for each ID
    float confSphere = 0, confCube = 0;
    for (const auto& r : results) {
        if (r.id == 1) confSphere = r.confidence;
        if (r.id == 3) confCube = r.confidence;
    }
    
    // Sphere (ID 1) should match better than cube (ID 3)
    EXPECT_GT(confSphere, confCube);
}

TEST_F(MatchingTest, ConfidenceInRange) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(cloud1_);
    
    MatchingIndex index(codebook_.get());
    index.add(desc, 1);
    
    auto results = index.query(desc, 1);  // Query with same descriptor
    
    ASSERT_EQ(results.size(), 1);
    EXPECT_GE(results[0].confidence, 0.0f);
    EXPECT_LE(results[0].confidence, 100.0f);
}

TEST_F(MatchingTest, ClearIndex) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(cloud1_);
    
    MatchingIndex index(codebook_.get());
    index.add(desc, 1);
    EXPECT_EQ(index.size(), 1);
    
    index.clear();
    EXPECT_EQ(index.size(), 0);
}

TEST_F(MatchingTest, MultipleDescriptors) {
    DescriptorExtractor extractor;
    
    MatchingIndex index(codebook_.get());
    
    for (int i = 0; i < 10; ++i) {
        // Create slight variations
        auto cloud = createSphereCloud(1.0f + i * 0.1f, 0, 0, 0);
        Descriptor desc = extractor.extract(cloud);
        index.add(desc, i);
    }
    
    EXPECT_EQ(index.size(), 10);
    
    // Query should return up to k results
    Descriptor query = extractor.extract(cloud1_);
    auto results = index.query(query, 5);
    
    EXPECT_EQ(results.size(), 5);
}

} // namespace test
} // namespace aum
