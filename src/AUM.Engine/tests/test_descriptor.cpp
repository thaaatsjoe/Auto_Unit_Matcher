// Descriptor Extraction Unit Tests
// Tests for FPFH descriptor extraction

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
        // Create a simple test point cloud (cube corners + center points)
        testCloud_ = std::make_shared<PointCloud>();
        
        // Add cube corners
        for (float x = -1.0f; x <= 1.0f; x += 2.0f) {
            for (float y = -1.0f; y <= 1.0f; y += 2.0f) {
                for (float z = -1.0f; z <= 1.0f; z += 2.0f) {
                    pcl::PointXYZ pt;
                    pt.x = x; pt.y = y; pt.z = z;
                    testCloud_->push_back(pt);
                }
            }
        }
        
        // Add more points to make a valid point cloud for normal estimation
        // Add face centers
        testCloud_->push_back(pcl::PointXYZ(0, 0, 1));
        testCloud_->push_back(pcl::PointXYZ(0, 0, -1));
        testCloud_->push_back(pcl::PointXYZ(0, 1, 0));
        testCloud_->push_back(pcl::PointXYZ(0, -1, 0));
        testCloud_->push_back(pcl::PointXYZ(1, 0, 0));
        testCloud_->push_back(pcl::PointXYZ(-1, 0, 0));
        
        // Add edge centers  
        for (int i = 0; i < 50; ++i) {
            float theta = static_cast<float>(i) / 50.0f * 6.28f;
            testCloud_->push_back(pcl::PointXYZ(
                std::cos(theta),
                std::sin(theta),
                0.0f
            ));
        }
        
        testCloud_->width = static_cast<uint32_t>(testCloud_->size());
        testCloud_->height = 1;
        testCloud_->is_dense = true;
    }
    
    PointCloudPtr testCloud_;
};

TEST_F(DescriptorTest, ExtractFromValidCloud) {
    DescriptorExtractor extractor;
    
    // This should not throw
    Descriptor desc = extractor.extract(testCloud_);
    
    // Should have features
    auto features = desc.getFeatures();
    EXPECT_TRUE(features != nullptr);
    EXPECT_GT(features->size(), 0);
}

TEST_F(DescriptorTest, AggregatedVectorDimension) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(testCloud_);
    
    auto vec = desc.getAggregatedVector();
    EXPECT_EQ(vec.size(), 33);  // FPFH has 33 bins
}

TEST_F(DescriptorTest, AggregatedVectorIsNormalized) {
    DescriptorExtractor extractor;
    Descriptor desc = extractor.extract(testCloud_);
    
    auto vec = desc.getAggregatedVector();
    
    // L2 norm should be approximately 1.0
    float norm = 0.0f;
    for (float v : vec) {
        norm += v * v;
    }
    norm = std::sqrt(norm);
    
    EXPECT_NEAR(norm, 1.0f, 0.01f);
}

TEST_F(DescriptorTest, SerializeDeserialize) {
    DescriptorExtractor extractor;
    Descriptor original = extractor.extract(testCloud_);
    
    // Serialize
    auto blob = original.serialize();
    EXPECT_GT(blob.size(), 4);  // At least count + some data
    
    // Deserialize
    Descriptor restored = Descriptor::deserialize(blob.data(), blob.size());
    
    // Compare aggregated vectors
    auto vec1 = original.getAggregatedVector();
    auto vec2 = restored.getAggregatedVector();
    
    ASSERT_EQ(vec1.size(), vec2.size());
    for (size_t i = 0; i < vec1.size(); ++i) {
        EXPECT_NEAR(vec1[i], vec2[i], 0.001f);
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
    config.fpfhRadius = 2.0f;
    config.numPoints = 500;
    
    DescriptorExtractor extractor(config);
    
    // Should work with custom config
    Descriptor desc = extractor.extract(testCloud_);
    EXPECT_TRUE(desc.getFeatures() != nullptr);
}

} // namespace test
} // namespace aum
