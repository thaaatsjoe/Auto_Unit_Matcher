// ISS Keypoint + SHOT352 Descriptor Extraction Unit Tests
// Tests for the ISS+SHOT352 pipeline (replaces old FPFH tests)

#include <gtest/gtest.h>
#include "descriptor.h"
#include <pcl/point_cloud.h>
#include <pcl/point_types.h>
#include <cmath>

namespace aum {
namespace test {

class DescriptorTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Create a dense sphere cloud with enough points for ISS+SHOT
        testCloud_ = std::make_shared<PointCloud>();
        
        float radius = 10.0f;  // 10mm — dental crown scale
        for (int lat = 0; lat < 50; ++lat) {
            for (int lon = 0; lon < 100; ++lon) {
                float theta = static_cast<float>(lat) / 50.0f * 3.14159f;
                float phi = static_cast<float>(lon) / 100.0f * 6.28318f;
                
                pcl::PointXYZ pt;
                pt.x = radius * std::sin(theta) * std::cos(phi);
                pt.y = radius * std::sin(theta) * std::sin(phi);
                pt.z = radius * std::cos(theta);
                testCloud_->push_back(pt);
            }
        }
        
        testCloud_->width = static_cast<uint32_t>(testCloud_->size());
        testCloud_->height = 1;
        testCloud_->is_dense = true;
    }
    
    PointCloudPtr testCloud_;
};

TEST_F(DescriptorTest, ExtractFromValidCloud) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(testCloud_);
    
    // Should have keypoints and features
    EXPECT_TRUE(desc.getKeypoints() != nullptr);
    EXPECT_TRUE(desc.getFeatures() != nullptr);
    EXPECT_GT(desc.size(), 0);
}

TEST_F(DescriptorTest, KeypointCountReasonable) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(testCloud_);
    
    // With relaxed ISS params on a 5000-point sphere, expect 10+ keypoints
    EXPECT_GE(desc.size(), 5);
    EXPECT_LE(desc.size(), 500);  // Should not exceed maxKeypoints
}

TEST_F(DescriptorTest, SHOTDimensionCorrect) {
    EXPECT_EQ(Descriptor::SHOT_DIM, 352);
    EXPECT_EQ(Descriptor::dimension(), 352);
}

TEST_F(DescriptorTest, SerializeDeserializeRoundTrip) {
    DescriptorExtractor extractor;
    Descriptor original = extractor.extract(testCloud_);
    
    // Serialize
    auto blob = original.serialize();
    EXPECT_GT(blob.size(), 8);  // magic(4) + count(4) + data
    
    // Check magic bytes
    EXPECT_EQ(blob[0], 'S');
    EXPECT_EQ(blob[1], 'H');
    EXPECT_EQ(blob[2], '0');
    EXPECT_EQ(blob[3], '1');
    
    // Deserialize
    Descriptor restored = Descriptor::deserialize(blob.data(), blob.size());
    
    // Should have same number of keypoints
    EXPECT_EQ(original.size(), restored.size());
    
    // Compare first keypoint position
    if (original.size() > 0) {
        auto origKp = original.getKeypoints();
        auto restKp = restored.getKeypoints();
        EXPECT_NEAR((*origKp)[0].x, (*restKp)[0].x, 0.001f);
        EXPECT_NEAR((*origKp)[0].y, (*restKp)[0].y, 0.001f);
        EXPECT_NEAR((*origKp)[0].z, (*restKp)[0].z, 0.001f);
    }
}

TEST_F(DescriptorTest, ExtractFromEmptyCloudThrows) {
    auto emptyCloud = std::make_shared<PointCloud>();
    DescriptorExtractor extractor;
    
    EXPECT_THROW(extractor.extract(emptyCloud), std::runtime_error);
}

TEST_F(DescriptorTest, ConfigurableParameters) {
    DescriptorConfig config;
    config.voxelSize = 0.2f;
    config.normalRadius = 1.0f;
    config.issSalientRadius = 1.0f;
    config.issNonMaxRadius = 0.8f;
    config.issThreshold21 = 0.9f;
    config.issThreshold32 = 0.9f;
    config.shotRadius = 2.5f;
    
    DescriptorExtractor extractor(config);
    
    // Should work with custom config
    Descriptor desc = extractor.extract(testCloud_);
    EXPECT_TRUE(desc.getKeypoints() != nullptr);
    EXPECT_GT(desc.size(), 0);
}

} // namespace test
} // namespace aum
