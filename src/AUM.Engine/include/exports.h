#pragma once

// Export/Import macro for Windows DLL
#ifdef _WIN32
    #ifdef AUM_ENGINE_EXPORTS
        #define AUM_API __declspec(dllexport)
    #else
        #define AUM_API __declspec(dllimport)
    #endif
#else
    #define AUM_API
#endif

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

// Handle types (opaque pointers)
typedef void* AUM_PointCloudHandle;
typedef void* AUM_DescriptorHandle;
typedef void* AUM_IndexHandle;

// Error codes
typedef enum {
    AUM_SUCCESS = 0,
    AUM_ERROR_NULL_POINTER = -1,
    AUM_ERROR_FILE_NOT_FOUND = -2,
    AUM_ERROR_INVALID_FORMAT = -3,
    AUM_ERROR_COMPUTATION_FAILED = -4,
    AUM_ERROR_OUT_OF_MEMORY = -5,
    AUM_ERROR_INDEX_NOT_READY = -6,
    AUM_ERROR_INVALID_HANDLE = -7
} AUM_ErrorCode;

// ============================================================================
// Result Structures
// ============================================================================

// Vote result from Stage 1 FAISS voting
typedef struct {
    int64_t unitId;       // Database ID of the unit
    float   voteScore;    // Weighted vote score
    int     voteCount;    // Raw number of keypoint votes
} AUM_VoteResult;

// Verification result from Stage 2 RANSAC + Dense ICP
typedef struct {
    int64_t unitId;
    float   ransacInlierRatio;
    float   icpFitnessScore;
    float   finalScore;           // ICP-based confidence 0-100
    int     ransacInliers;
    int     correspondences;
} AUM_VerificationResult;

// ============================================================================
// STL Parsing
// ============================================================================

/**
 * Parse an STL file into a point cloud.
 * @param path Path to STL file (UTF-8 encoded)
 * @param out_handle Output handle to point cloud (caller must free with aum_free_point_cloud)
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_parse_stl(const char* path, AUM_PointCloudHandle* out_handle);

/**
 * Get the number of points in a point cloud.
 */
AUM_API AUM_ErrorCode aum_point_cloud_size(AUM_PointCloudHandle handle, size_t* out_size);

/**
 * Free a point cloud handle.
 */
AUM_API void aum_free_point_cloud(AUM_PointCloudHandle handle);

// ============================================================================
// Descriptor Extraction (ISS Keypoints + SHOT352 + Dense Cloud)
// ============================================================================

/**
 * Extract ISS keypoint + SHOT352 descriptors + dense cloud from a point cloud.
 * @param pc Point cloud handle
 * @param out_handle Output descriptor handle (caller must free with aum_free_descriptor)
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_extract_descriptors(AUM_PointCloudHandle pc, AUM_DescriptorHandle* out_handle);

/**
 * Serialize descriptor to binary blob for database storage.
 * Blob format: SH02 (keypoints + SHOT352 + dense cloud + normals)
 */
AUM_API AUM_ErrorCode aum_serialize_descriptor(AUM_DescriptorHandle desc, uint8_t** out_blob, size_t* out_len);

/**
 * Deserialize descriptor from binary blob (supports SH01 and SH02).
 */
AUM_API AUM_ErrorCode aum_deserialize_descriptor(const uint8_t* blob, size_t len, AUM_DescriptorHandle* out_handle);

/**
 * Get the number of keypoints in a descriptor.
 */
AUM_API AUM_ErrorCode aum_descriptor_size(AUM_DescriptorHandle desc, size_t* out_size);

/**
 * Free a descriptor handle.
 */
AUM_API void aum_free_descriptor(AUM_DescriptorHandle handle);

/**
 * Free a serialized blob.
 */
AUM_API void aum_free_blob(uint8_t* blob);

// ============================================================================
// Matching / FAISS Index (Local Feature Voting)
// ============================================================================

/**
 * Create a new FAISS voting index for SHOT352 descriptors.
 * @param out_handle Output index handle (caller must free with aum_free_index)
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_create_index(AUM_IndexHandle* out_handle);

/**
 * Add a descriptor's keypoints to the index.
 * Must be called before aum_train_index.
 * @param idx Index handle
 * @param desc Descriptor handle
 * @param unitId Database ID of the unit
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_add_to_index(AUM_IndexHandle idx, AUM_DescriptorHandle desc, int64_t unitId);

/**
 * Train the IVF index. Must be called after all add() calls and before querying.
 * Builds IndexIVFFlat with nlist = sqrt(ntotal) clusters.
 * @param idx Index handle
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_train_index(AUM_IndexHandle idx);

/**
 * Stage 1: Query the index using keypoint voting.
 * @param idx Index handle (must be trained)
 * @param query Query descriptor (from scan)
 * @param topK Number of top-voted units to return
 * @param out_results Output array of topK results (caller allocates)
 * @param out_count Actual number of results returned
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_query_votes(
    AUM_IndexHandle idx,
    AUM_DescriptorHandle query,
    int topK,
    AUM_VoteResult* out_results,
    int* out_count);

/**
 * Stage 2: Geometric verification using RANSAC + Dense Point-to-Plane ICP.
 * @param query Query descriptor (scan — partial, exterior only)
 * @param candidate Candidate descriptor (database unit — full STL)
 * @param out_result Output verification result
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_verify(
    AUM_DescriptorHandle query,
    AUM_DescriptorHandle candidate,
    AUM_VerificationResult* out_result);

/**
 * Save index to file.
 */
AUM_API AUM_ErrorCode aum_save_index(AUM_IndexHandle idx, const char* path);

/**
 * Load index from file.
 */
AUM_API AUM_ErrorCode aum_load_index(const char* path, AUM_IndexHandle* out_handle);

/**
 * Get index size (total feature vectors).
 */
AUM_API AUM_ErrorCode aum_index_size(AUM_IndexHandle idx, size_t* out_size);

/**
 * Check if index is trained.
 */
AUM_API AUM_ErrorCode aum_index_trained(AUM_IndexHandle idx, int* out_trained);

/**
 * Free an index handle.
 */
AUM_API void aum_free_index(AUM_IndexHandle handle);

// ============================================================================
// Utility
// ============================================================================

/**
 * Get the last error message (thread-local).
 */
AUM_API const char* aum_get_last_error();

/**
 * Get library version string.
 */
AUM_API const char* aum_get_version();

#ifdef __cplusplus
}
#endif
