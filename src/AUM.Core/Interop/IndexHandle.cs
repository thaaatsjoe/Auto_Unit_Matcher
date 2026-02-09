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
    /// Adds a descriptor to the index with the given ID.
    /// </summary>
    public void Add(DescriptorHandle descriptor, long id)
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        if (descriptor == null || descriptor.IsInvalid)
            throw new ArgumentException("Descriptor is null or invalid", nameof(descriptor));
        
        var result = NativeMethods.aum_add_to_index(handle, descriptor.DangerousGetHandle(), id);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to add to index: {result}");
    }
    
    /// <summary>
    /// Trains the index. Must be called after adding descriptors and before querying.
    /// </summary>
    public void Train()
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        
        var result = NativeMethods.aum_train_index(handle);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to train index: {result}");
    }
    
    /// <summary>
    /// Queries the index for similar descriptors.
    /// </summary>
    /// <param name="query">Query descriptor.</param>
    /// <param name="topK">Number of results to return (default 5 per PRD).</param>
    /// <returns>Array of match results, ordered by similarity.</returns>
    public MatchResult[] Query(DescriptorHandle query, int topK = 5)
    {
        if (IsInvalid)
            throw new ObjectDisposedException(nameof(IndexHandle));
        if (query == null || query.IsInvalid)
            throw new ArgumentException("Query descriptor is null or invalid", nameof(query));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");
        
        var results = new MatchResult[topK];
        var returnCode = NativeMethods.aum_query_index(handle, query.DangerousGetHandle(), topK, results, out var count);
        
        if (returnCode != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to query index: {returnCode}");
        
        // Return only the actual results
        if (count < topK)
        {
            var trimmed = new MatchResult[count];
            Array.Copy(results, trimmed, count);
            return trimmed;
        }
        
        return results;
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
            throw new InvalidOperationException($"Failed to save index: {result}");
    }
    
    /// <summary>
    /// Creates a new empty index.
    /// </summary>
    public static IndexHandle Create()
    {
        var result = NativeMethods.aum_create_index(out var indexPtr);
        if (result != ErrorCode.Success)
            throw new InvalidOperationException($"Failed to create index: {result}");
        
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
            throw new InvalidOperationException($"Failed to load index: {result}");
        
        return new IndexHandle(indexPtr);
    }
}
