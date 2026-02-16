using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// SafeHandle wrapper for native FAISS index handles.
/// Ensures proper cleanup via aum_free_index.
/// </summary>
public sealed class IndexHandle : SafeHandle
{
    /// <summary>
    /// Creates a new IndexHandle.
    /// </summary>
    public IndexHandle() : base(IntPtr.Zero, true)
    {
    }
    
    /// <summary>
    /// Creates an IndexHandle wrapping an existing native handle.
    /// </summary>
    internal IndexHandle(IntPtr handle) : base(IntPtr.Zero, true)
    {
        SetHandle(handle);
    }
    
    /// <inheritdoc/>
    public override bool IsInvalid => handle == IntPtr.Zero;
    
    /// <inheritdoc/>
    protected override bool ReleaseHandle()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.aum_free_index(handle);
            handle = IntPtr.Zero;
        }
        return true;
    }
    
    /// <summary>
    /// Adds a descriptor's keypoints to the index.
    /// Must be called before Train().
    /// </summary>
    public void Add(DescriptorHandle descriptor, long unitId)
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        if (descriptor == null || descriptor.IsInvalid)
            throw new ArgumentException("Descriptor is null or invalid", nameof(descriptor));
        
        var result = NativeMethods.aum_add_to_index(handle, descriptor.DangerousGetHandle(), unitId);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to add to index: {result} - {NativeMethods.GetLastErrorMessage()}");
    }
    
    /// <summary>
    /// Trains the IVF index. Must be called after adding all descriptors and before querying.
    /// </summary>
    public void Train()
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        
        var result = NativeMethods.aum_train_index(handle);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to train index: {result} - {NativeMethods.GetLastErrorMessage()}");
    }
    
    /// <summary>
    /// Stage 1: Query the index using keypoint voting.
    /// Returns top-K units by weighted vote score.
    /// </summary>
    public VoteResult[] QueryVotes(DescriptorHandle query, int topK = 10)
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        if (query == null || query.IsInvalid)
            throw new ArgumentException("Query descriptor is null or invalid", nameof(query));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");
        
        var results = new VoteResult[topK];
        var returnCode = NativeMethods.aum_query_votes(handle, query.DangerousGetHandle(), topK, results, out var count);
        
        if (returnCode != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to query votes: {returnCode} - {NativeMethods.GetLastErrorMessage()}");
        
        if (count < topK)
        {
            var trimmed = new VoteResult[count];
            Array.Copy(results, trimmed, count);
            return trimmed;
        }
        
        return results;
    }
    
    /// <summary>
    /// Stage 2: Geometric verification using RANSAC + Dense Point-to-Plane ICP.
    /// </summary>
    public static VerificationResult Verify(DescriptorHandle query, DescriptorHandle candidate)
    {
        if (query == null || query.IsInvalid)
            throw new ArgumentException("Query descriptor is null or invalid", nameof(query));
        if (candidate == null || candidate.IsInvalid)
            throw new ArgumentException("Candidate descriptor is null or invalid", nameof(candidate));
        
        var result = NativeMethods.aum_verify(query.DangerousGetHandle(), candidate.DangerousGetHandle(), out var verification);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to verify: {result} - {NativeMethods.GetLastErrorMessage()}");
        
        return verification;
    }
    
    /// <summary>
    /// Saves the index to a file.
    /// </summary>
    public void Save(string path)
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        
        var result = NativeMethods.aum_save_index(handle, path);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to save index: {result} - {NativeMethods.GetLastErrorMessage()}");
    }
    
    /// <summary>
    /// Creates a new empty FAISS voting index.
    /// </summary>
    public static IndexHandle Create()
    {
        var result = NativeMethods.aum_create_index(out var indexPtr);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to create index: {result} - {NativeMethods.GetLastErrorMessage()}");
        
        return new IndexHandle(indexPtr);
    }
    
    /// <summary>
    /// Loads an index from a file.
    /// </summary>
    public static IndexHandle Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        
        var result = NativeMethods.aum_load_index(path, out var indexPtr);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to load index: {result} - {NativeMethods.GetLastErrorMessage()}");
        
        return new IndexHandle(indexPtr);
    }
}
