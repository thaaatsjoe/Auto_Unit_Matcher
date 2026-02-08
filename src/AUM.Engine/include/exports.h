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

// Match result structure
typedef struct {
    int64_t id;           // Database ID of matched unit
    float distance;       // Distance (lower = better match)
    float confidence;     // Confidence score (0-100%)
} AUM_MatchResult;

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
// Descriptor Extraction
// ============================================================================

/**
 * Extract FPFH descriptors from a point cloud.
 * @param pc Point cloud handle
 * @param out_handle Output descriptor handle (caller must free with aum_free_descriptor)
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_extract_descriptors(AUM_PointCloudHandle pc, AUM_DescriptorHandle* out_handle);

/**
 * Serialize descriptor to binary blob for database storage.
 * @param desc Descriptor handle
 * @param out_blob Output blob (caller must free with aum_free_blob)
 * @param out_len Output blob length
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_serialize_descriptor(AUM_DescriptorHandle desc, uint8_t** out_blob, size_t* out_len);

/**
 * Deserialize descriptor from binary blob.
 * @param blob Binary blob data
 * @param len Blob length
 * @param out_handle Output descriptor handle
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_deserialize_descriptor(const uint8_t* blob, size_t len, AUM_DescriptorHandle* out_handle);

/**
 * Free a descriptor handle.
 */
AUM_API void aum_free_descriptor(AUM_DescriptorHandle handle);

/**
 * Free a serialized blob.
 */
AUM_API void aum_free_blob(uint8_t* blob);

// ============================================================================
// Matching / FAISS Index
// ============================================================================

/**
 * Create a new FAISS index for similarity search.
 * @param out_handle Output index handle (caller must free with aum_free_index)
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_create_index(AUM_IndexHandle* out_handle);

/**
 * Add a descriptor to the index.
 * @param idx Index handle
 * @param desc Descriptor handle
 * @param id Unique ID to associate with this descriptor
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_add_to_index(AUM_IndexHandle idx, AUM_DescriptorHandle desc, int64_t id);

/**
 * Train the index (call after adding all descriptors, before querying).
 * @param idx Index handle
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_train_index(AUM_IndexHandle idx);

/**
 * Query the index for similar descriptors.
 * @param idx Index handle
 * @param query Query descriptor
 * @param k Number of results to return
 * @param out_results Output array of k results (caller allocates)
 * @param out_count Actual number of results returned
 * @return AUM_SUCCESS or error code
 */
AUM_API AUM_ErrorCode aum_query_index(
    AUM_IndexHandle idx, 
    AUM_DescriptorHandle query, 
    int k, 
    AUM_MatchResult* out_results,
    int* out_count
);

/**
 * Save index to file.
 */
AUM_API AUM_ErrorCode aum_save_index(AUM_IndexHandle idx, const char* path);

/**
 * Load index from file.
 */
AUM_API AUM_ErrorCode aum_load_index(const char* path, AUM_IndexHandle* out_handle);

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
