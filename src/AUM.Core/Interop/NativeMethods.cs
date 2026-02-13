using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// P/Invoke declarations for the native AUM.Engine.dll.
/// All functions are defined in exports.h.
/// </summary>
internal static class NativeMethods
{
    private const string DllName = "AUM.Engine.dll";
    
    // ============================================================================
    // STL Parsing
    // ============================================================================
    
    /// <summary>
    /// Parse an STL file into a point cloud.
    /// </summary>
    /// <param name="path">Path to STL file (UTF-8 encoded).</param>
    /// <param name="outHandle">Output handle to point cloud (caller must free with aum_free_point_cloud).</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern ErrorCode aum_parse_stl(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr outHandle);
    
    /// <summary>
    /// Get the number of points in a point cloud.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_point_cloud_size(IntPtr handle, out nuint outSize);
    
    /// <summary>
    /// Free a point cloud handle.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_point_cloud(IntPtr handle);
    
    // ============================================================================
    // Descriptor Extraction
    // ============================================================================
    
    /// <summary>
    /// Extract FPFH descriptors from a point cloud.
    /// </summary>
    /// <param name="pc">Point cloud handle.</param>
    /// <param name="outHandle">Output descriptor handle (caller must free with aum_free_descriptor).</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_extract_descriptors(IntPtr pc, out IntPtr outHandle);
    
    /// <summary>
    /// Serialize descriptor to binary blob for database storage.
    /// </summary>
    /// <param name="desc">Descriptor handle.</param>
    /// <param name="outBlob">Output blob (caller must free with aum_free_blob).</param>
    /// <param name="outLen">Output blob length.</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_serialize_descriptor(IntPtr desc, out IntPtr outBlob, out nuint outLen);
    
    /// <summary>
    /// Deserialize descriptor from binary blob.
    /// </summary>
    /// <param name="blob">Binary blob data.</param>
    /// <param name="len">Blob length.</param>
    /// <param name="outHandle">Output descriptor handle.</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_deserialize_descriptor(IntPtr blob, nuint len, out IntPtr outHandle);
    
    /// <summary>
    /// Free a descriptor handle.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_descriptor(IntPtr handle);
    
    /// <summary>
    /// Free a serialized blob.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_blob(IntPtr blob);
    
    // ============================================================================
    // Codebook (Bag-of-Words)
    // ============================================================================
    
    /// <summary>
    /// Load a trained codebook from file.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern ErrorCode aum_load_codebook(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr outHandle);
    
    /// <summary>
    /// Free a codebook handle.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_codebook(IntPtr handle);
    
    /// <summary>
    /// Get the number of clusters (K) in the codebook.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_codebook_k(IntPtr cb, out int outK);
    
    // ============================================================================
    // Matching / FAISS Index
    // ============================================================================
    
    /// <summary>
    /// Create a new FAISS index using a codebook for histogram-based matching.
    /// </summary>
    /// <param name="codebook">Codebook handle (must remain valid for lifetime of index).</param>
    /// <param name="outHandle">Output index handle (caller must free with aum_free_index).</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_create_index(IntPtr codebook, out IntPtr outHandle);
    
    /// <summary>
    /// Add a descriptor to the index.
    /// </summary>
    /// <param name="idx">Index handle.</param>
    /// <param name="desc">Descriptor handle.</param>
    /// <param name="id">Unique ID to associate with this descriptor.</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_add_to_index(IntPtr idx, IntPtr desc, long id);
    
    /// <summary>
    /// Train the index (call after adding all descriptors, before querying).
    /// </summary>
    /// <param name="idx">Index handle.</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_train_index(IntPtr idx);
    
    /// <summary>
    /// Query the index for similar descriptors.
    /// </summary>
    /// <param name="idx">Index handle.</param>
    /// <param name="query">Query descriptor.</param>
    /// <param name="k">Number of results to return.</param>
    /// <param name="outResults">Output array of k results (caller allocates).</param>
    /// <param name="outCount">Actual number of results returned.</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_query_index(
        IntPtr idx,
        IntPtr query,
        int k,
        [Out] MatchResult[] outResults,
        out int outCount);
    
    /// <summary>
    /// Save index to file.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern ErrorCode aum_save_index(
        IntPtr idx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    
    /// <summary>
    /// Load index from file.
    /// </summary>
    /// <param name="path">Path to saved index file.</param>
    /// <param name="codebook">Codebook handle (needed for subsequent queries).</param>
    /// <param name="outHandle">Output index handle.</param>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern ErrorCode aum_load_index(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        IntPtr codebook,
        out IntPtr outHandle);
    
    /// <summary>
    /// Compare two descriptors point-to-point for partial matching.
    /// Returns a score (0-100%) based on how many query points match candidate points.
    /// </summary>
    /// <param name="query">Query descriptor handle (e.g. partial scan).</param>
    /// <param name="candidate">Candidate descriptor handle (e.g. full model from DB).</param>
    /// <param name="outScore">Output match score (0-100).</param>
    /// <returns>AUM_SUCCESS or error code.</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_compare_descriptors(
        IntPtr query,
        IntPtr candidate,
        out float outScore);
    
    /// <summary>
    /// Free an index handle.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_index(IntPtr handle);
    
    // ============================================================================
    // Utility
    // ============================================================================
    
    /// <summary>
    /// Get the last error message (thread-local).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr aum_get_last_error();
    
    /// <summary>
    /// Get library version string.
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr aum_get_version();
    
    // ============================================================================
    // Helper Methods
    // ============================================================================
    
    /// <summary>
    /// Gets the last error message as a managed string.
    /// </summary>
    public static string GetLastErrorMessage()
    {
        var ptr = aum_get_last_error();
        return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ptr) ?? string.Empty;
    }
    
    /// <summary>
    /// Gets the library version as a managed string.
    /// </summary>
    public static string GetVersionString()
    {
        var ptr = aum_get_version();
        return ptr == IntPtr.Zero ? "unknown" : Marshal.PtrToStringUTF8(ptr) ?? "unknown";
    }
}
