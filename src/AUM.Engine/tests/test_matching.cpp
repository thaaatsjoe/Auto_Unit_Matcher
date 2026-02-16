// Matching Index Unit Tests
// Tests for FAISS voting + RANSAC + Dense ICP pipeline

#include <gtest/gtest.h>
#include "matching.h"
#include "descriptor.h"
#include <pcl/point_cloud.h>
#include <pcl/point_types.h>
#include <cmath>

namespace aum {
namespace test {

class MatchingTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Create test point clouds at dental-crown scale (~10-20mm)
        cloud1_ = createSphereCloud(10.0f, 0.0f, 0.0f, 0.0f);
        cloud2_ = createSphereCloud(10.0f, 0.5f, 0.0f, 0.0f);  // Similar, slightly offset
        cloud3_ = createCubeCloud(20.0f, 50.0f, 0.0f, 0.0f);   // Different shape, far away
    }
    
    PointCloudPtr createSphereCloud(float radius, float cx, float cy, float cz) {
        auto cloud = std::make_shared<PointCloud>();
        
        for (int lat = 0; lat < 50; ++lat) {
            for (int lon = 0; lon < 100; ++lon) {
                float theta = static_cast<float>(lat) / 50.0f * 3.14159f;
                float phi = static_cast<float>(lon) / 100.0f * 6.28318f;
                
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
        
        float step = size / 20.0f;
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
};

TEST_F(MatchingTest, CreateIndex) {
    MatchingIndex index(Descriptor::SHOT_DIM);
    EXPECT_EQ(index.size(), 0);
}

TEST_F(MatchingTest, AddToIndex) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(cloud1_);
    
    MatchingIndex index(Descriptor::SHOT_DIM);
    index.add(desc, 100);
    
    // Size is number of keypoint vectors buffered (not trained yet)
    EXPECT_GT(index.size(), 0);
}

TEST_F(MatchingTest, TrainIndex) {
    DescriptorExtractor extractor;
    Descriptor desc1 = extractor.extract(cloud1_);
    Descriptor desc2 = extractor.extract(cloud2_);
    Descriptor desc3 = extractor.extract(cloud3_);
    
    MatchingIndex index(Descriptor::SHOT_DIM);
    index.add(desc1, 1);
    index.add(desc2, 2);
    index.add(desc3, 3);
    
    // Should not throw
    EXPECT_NO_THROW(index.trainIndex());
    EXPECT_TRUE(index.isTrained());
}

TEST_F(MatchingTest, QueryBeforeTrainThrows) {
    DescriptorExtractor extractor;
    Descriptor query = extractor.extract(cloud1_);
    
    MatchingIndex index(Descriptor::SHOT_DIM);
    
    EXPECT_THROW(index.queryVotes(query, 5), std::runtime_error);
}

TEST_F(MatchingTest, QueryVotesReturnsResults) {
    DescriptorExtractor extractor;
    Descriptor desc1 = extractor.extract(cloud1_);
    Descriptor desc2 = extractor.extract(cloud2_);
    Descriptor desc3 = extractor.extract(cloud3_);
    
    MatchingIndex index(Descriptor::SHOT_DIM);
    index.add(desc1, 1);
    index.add(desc2, 2);
    index.add(desc3, 3);
    index.trainIndex();
    
    auto results = index.queryVotes(desc1, 5);
    EXPECT_GT(results.size(), 0);
    
    // The self-match (unit 1) should receive votes
    bool foundSelf = false;
    for (const auto& r : results) {
        if (r.unitId == 1) {
            foundSelf = true;
            EXPECT_GT(r.voteScore, 0.0f);
            EXPECT_GT(r.voteCount, 0);
        }
    }
    EXPECT_TRUE(foundSelf);
}

TEST_F(MatchingTest, VerifyMatchingPair) {
    DescriptorExtractor extractor;
    Descriptor desc1 = extractor.extract(cloud1_);
    Descriptor desc2 = extractor.extract(cloud2_);
    
    // Verify two similar shapes
    auto result = MatchingIndex::verify(desc1, desc2);
    
    EXPECT_GT(result.correspondences, 0);
    EXPECT_GE(result.finalScore, 0.0f);
    EXPECT_LE(result.finalScore, 100.0f);
}

TEST_F(MatchingTest, ClearIndex) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(cloud1_);
    
    MatchingIndex index(Descriptor::SHOT_DIM);
    index.add(desc, 1);
    EXPECT_GT(index.size(), 0);
    
    index.clear();
    EXPECT_EQ(index.size(), 0);
}

} // namespace test
} // namespace aum
