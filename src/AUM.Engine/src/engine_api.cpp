// C API Implementation - Exports for P/Invoke
// Wraps C++ classes in C-compatible interface
// v4.0: ISS + SHOT352 + Voting + RANSAC + Dense Point-to-Plane ICP

#include "exports.h"
#include "stl_parser.h"
#include "descriptor.h"
#include "matching.h"
#include <string>
#include <memory>

// Thread-local error message
static thread_local std::string g_lastError;

// Version string
static const char* VERSION = "4.0.0";

// Helper to set error and return
static AUM_ErrorCode setError(AUM_ErrorCode code, const std::string& msg) {
    g_lastError = msg;
    return code;
}

// ============================================================================
// STL Parsing
// ============================================================================

AUM_API AUM_ErrorCode aum_parse_stl(const char* path, AUM_PointCloudHandle* out_handle) {
    if (!path || !out_handle) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto cloud = aum::STLParser::load(path);
        *out_handle = new aum::PointCloudPtr(cloud);
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_FILE_NOT_FOUND, e.what());
    }
}

AUM_API AUM_ErrorCode aum_point_cloud_size(AUM_PointCloudHandle handle, size_t* out_size) {
    if (!handle || !out_size) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    auto cloud = *static_cast<aum::PointCloudPtr*>(handle);
    *out_size = cloud->size();
    return AUM_SUCCESS;
}

AUM_API void aum_free_point_cloud(AUM_PointCloudHandle handle) {
    if (handle) {
        delete static_cast<aum::PointCloudPtr*>(handle);
    }
}

// ============================================================================
// Descriptor Extraction (ISS Keypoints + SHOT352 + Dense Cloud)
// ============================================================================

AUM_API AUM_ErrorCode aum_extract_descriptors(AUM_PointCloudHandle pc, AUM_DescriptorHandle* out_handle) {
    if (!pc || !out_handle) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto cloud = *static_cast<aum::PointCloudPtr*>(pc);
        aum::DescriptorExtractor extractor;
        auto desc = extractor.extract(cloud);
        *out_handle = new aum::Descriptor(std::move(desc));
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_serialize_descriptor(AUM_DescriptorHandle desc, uint8_t** out_blob, size_t* out_len) {
    if (!desc || !out_blob || !out_len) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto descriptor = static_cast<aum::Descriptor*>(desc);
        auto blob = descriptor->serialize();
        
        *out_len = blob.size();
        *out_blob = new uint8_t[blob.size()];
        std::memcpy(*out_blob, blob.data(), blob.size());
        
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_deserialize_descriptor(const uint8_t* blob, size_t len, AUM_DescriptorHandle* out_handle) {
    if (!blob || !out_handle) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto desc = aum::Descriptor::deserialize(blob, len);
        *out_handle = new aum::Descriptor(std::move(desc));
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_INVALID_FORMAT, e.what());
    }
}

AUM_API AUM_ErrorCode aum_descriptor_size(AUM_DescriptorHandle desc, size_t* out_size) {
    if (!desc || !out_size) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    auto descriptor = static_cast<aum::Descriptor*>(desc);
    *out_size = descriptor->size();
    return AUM_SUCCESS;
}

AUM_API void aum_free_descriptor(AUM_DescriptorHandle handle) {
    if (handle) {
        delete static_cast<aum::Descriptor*>(handle);
    }
}

AUM_API void aum_free_blob(uint8_t* blob) {
    delete[] blob;
}

// ============================================================================
// Matching / FAISS Index (Local Feature Voting)
// ============================================================================

AUM_API AUM_ErrorCode aum_create_index(AUM_IndexHandle* out_handle) {
    if (!out_handle) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        *out_handle = new aum::MatchingIndex();
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_OUT_OF_MEMORY, e.what());
    }
}

AUM_API AUM_ErrorCode aum_add_to_index(AUM_IndexHandle idx, AUM_DescriptorHandle desc, int64_t unitId) {
    if (!idx || !desc) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto index = static_cast<aum::MatchingIndex*>(idx);
        auto descriptor = static_cast<aum::Descriptor*>(desc);
        index->add(*descriptor, unitId);
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_train_index(AUM_IndexHandle idx) {
    if (!idx) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto index = static_cast<aum::MatchingIndex*>(idx);
        index->trainIndex();
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_query_votes(
    AUM_IndexHandle idx,
    AUM_DescriptorHandle query,
    int topK,
    AUM_VoteResult* out_results,
    int* out_count
) {
    if (!idx || !query || !out_results || !out_count) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto index = static_cast<aum::MatchingIndex*>(idx);
        auto descriptor = static_cast<aum::Descriptor*>(query);
        
        auto results = index->queryVotes(*descriptor, topK);
        
        *out_count = static_cast<int>(results.size());
        for (size_t i = 0; i < results.size(); ++i) {
            out_results[i].unitId = results[i].unitId;
            out_results[i].voteScore = results[i].voteScore;
            out_results[i].voteCount = results[i].voteCount;
        }
        
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_verify(
    AUM_DescriptorHandle query,
    AUM_DescriptorHandle candidate,
    AUM_VerificationResult* out_result
) {
    if (!query || !candidate || !out_result) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto queryDesc = static_cast<aum::Descriptor*>(query);
        auto candidateDesc = static_cast<aum::Descriptor*>(candidate);
        
        // Uses default parameters from matching.h:
        // ransacThreshold = 0.25mm, icpMaxCorrespondenceDist = 0.5mm, icpFitnessDecay = 0.5
        auto result = aum::MatchingIndex::verify(*queryDesc, *candidateDesc);
        
        out_result->unitId = result.unitId;
        out_result->ransacInlierRatio = result.ransacInlierRatio;
        out_result->icpFitnessScore = result.icpFitnessScore;
        out_result->finalScore = result.finalScore;
        out_result->ransacInliers = result.ransacInliers;
        out_result->correspondences = result.correspondences;
        
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_save_index(AUM_IndexHandle idx, const char* path) {
    if (!idx || !path) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto index = static_cast<aum::MatchingIndex*>(idx);
        index->save(path);
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_COMPUTATION_FAILED, e.what());
    }
}

AUM_API AUM_ErrorCode aum_load_index(const char* path, AUM_IndexHandle* out_handle) {
    if (!path || !out_handle) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    try {
        auto index = aum::MatchingIndex::load(path);
        *out_handle = new aum::MatchingIndex(std::move(index));
        return AUM_SUCCESS;
    } catch (const std::exception& e) {
        return setError(AUM_ERROR_FILE_NOT_FOUND, e.what());
    }
}

AUM_API AUM_ErrorCode aum_index_size(AUM_IndexHandle idx, size_t* out_size) {
    if (!idx || !out_size) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    auto index = static_cast<aum::MatchingIndex*>(idx);
    *out_size = index->size();
    return AUM_SUCCESS;
}

AUM_API AUM_ErrorCode aum_index_trained(AUM_IndexHandle idx, int* out_trained) {
    if (!idx || !out_trained) {
        return setError(AUM_ERROR_NULL_POINTER, "Null pointer argument");
    }
    
    auto index = static_cast<aum::MatchingIndex*>(idx);
    *out_trained = index->isTrained() ? 1 : 0;
    return AUM_SUCCESS;
}

AUM_API void aum_free_index(AUM_IndexHandle handle) {
    if (handle) {
        delete static_cast<aum::MatchingIndex*>(handle);
    }
}

// ============================================================================
// Utility
// ============================================================================

AUM_API const char* aum_get_last_error() {
    return g_lastError.c_str();
}

AUM_API const char* aum_get_version() {
    return VERSION;
}
