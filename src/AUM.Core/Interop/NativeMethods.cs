using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// Vote result from Stage 1 FAISS voting.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VoteResult
{
    public long UnitId;
    public float VoteScore;
    public int VoteCount;
}

/// <summary>
/// Verification result from Stage 2 RANSAC + Dense ICP.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VerificationResult
{
    public long UnitId;
    public float RansacInlierRatio;
    public float IcpFitnessScore;
    public float FinalScore;
    public int RansacInliers;
    public int Correspondences;
}

/// <summary>
/// P/Invoke declarations for the native AUM.Engine.dll (v4.0).
/// ISS Keypoints + SHOT352 + FAISS Voting + RANSAC + Dense Point-to-Plane ICP.
/// </summary>
public static class NativeMethods
{
    private const string DllName = "AUM.Engine";
    
    // ============================================================================
    // STL Parsing
    // ============================================================================
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_parse_stl(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr out_handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_point_cloud_size(IntPtr handle, out nuint out_size);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_point_cloud(IntPtr handle);
    
    // ============================================================================
    // Descriptor Extraction (ISS Keypoints + SHOT352 + Dense Cloud)
    // ============================================================================
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_extract_descriptors(IntPtr pc, out IntPtr out_handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_serialize_descriptor(IntPtr desc, out IntPtr out_blob, out nuint out_len);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_deserialize_descriptor(IntPtr blob, nuint len, out IntPtr out_handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_descriptor_size(IntPtr desc, out nuint out_size);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_descriptor(IntPtr handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_blob(IntPtr blob);
    
    // ============================================================================
    // Matching / FAISS Index (Local Feature Voting)
    // ============================================================================
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_create_index(out IntPtr out_handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_add_to_index(IntPtr idx, IntPtr desc, long unitId);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_train_index(IntPtr idx);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_query_votes(
        IntPtr idx,
        IntPtr query,
        int topK,
        [Out] VoteResult[] out_results,
        out int out_count);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_verify(
        IntPtr query,
        IntPtr candidate,
        out VerificationResult out_result);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_save_index(IntPtr idx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_load_index(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out IntPtr out_handle);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_index_size(IntPtr idx, out nuint out_size);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern ErrorCode aum_index_trained(IntPtr idx, out int out_trained);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void aum_free_index(IntPtr handle);
    
    // ============================================================================
    // Utility
    // ============================================================================
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr aum_get_last_error();
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr aum_get_version();
    
    /// <summary>
    /// Gets the last error message from the native engine.
    /// </summary>
    public static string GetLastErrorMessage()
    {
        var ptr = aum_get_last_error();
        return Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
    }
    
    /// <summary>
    /// Gets the native engine version string.
    /// </summary>
    public static string GetVersionString()
    {
        var ptr = aum_get_version();
        return Marshal.PtrToStringAnsi(ptr) ?? "unknown";
    }
}
