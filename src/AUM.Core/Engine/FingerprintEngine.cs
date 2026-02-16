using System.Threading;
using AUM.Core.Interop;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Engine;

/// <summary>
/// High-level wrapper for the native fingerprint engine.
/// v4.0: ISS Keypoints + SHOT352 + FAISS Voting + RANSAC + Dense Point-to-Plane ICP
/// No codebook required — local features indexed directly in FAISS.
/// </summary>
public sealed class FingerprintEngine : IFingerprintEngine
{
    private readonly ILogger<FingerprintEngine>? _logger;
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private IndexHandle? _index;
    private int _indexCount;
    private bool _disposed;
    
    /// <summary>
    /// Creates a new FingerprintEngine with a FAISS voting index.
    /// No codebook needed — SHOT352 features indexed directly.
    /// </summary>
    public FingerprintEngine(ILogger<FingerprintEngine>? logger = null)
    {
        _logger = logger;
        
        // Create empty index
        _index = IndexHandle.Create();
        _indexCount = 0;
        _logger?.LogInformation("FingerprintEngine v{Version} initialized (ISS+SHOT352+Voting+DenseICP)", Version);
    }
    
    /// <inheritdoc/>
    public string Version => NativeMethods.GetVersionString();
    
    /// <inheritdoc/>
    public int IndexCount => _indexCount;
    
    /// <inheritdoc/>
    public byte[] ExtractDescriptor(string stlPath)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(stlPath))
            throw new ArgumentException("STL path cannot be null or empty", nameof(stlPath));
        
        _logger?.LogDebug("Extracting ISS+SHOT352+DenseCloud descriptor from {Path}", stlPath);
        
        // Parse STL file
        var parseResult = NativeMethods.aum_parse_stl(stlPath, out var pointCloudPtr);
        EngineException.ThrowIfError(parseResult);
        
        using var pointCloud = new PointCloudHandle(pointCloudPtr);
        _logger?.LogDebug("Parsed STL with {Count} points", pointCloud.PointCount);
        
        // Extract ISS keypoints + SHOT352 descriptors + dense cloud
        var extractResult = NativeMethods.aum_extract_descriptors(pointCloud.DangerousGetHandle(), out var descriptorPtr);
        EngineException.ThrowIfError(extractResult);
        
        using var descriptor = new DescriptorHandle(descriptorPtr);
        
        // Serialize to SH02 blob (keypoints + SHOT + dense cloud + normals)
        var blob = descriptor.Serialize();
        _logger?.LogDebug("Extracted descriptor: {Size} bytes (SH02 format)", blob.Length);
        
        return blob;
    }
    
    /// <inheritdoc/>
    public VoteResult[] QueryVotes(byte[] descriptor, int topK = 10)
    {
        ThrowIfDisposed();
        
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _logger?.LogDebug("Stage 1: Voting for top {K} candidates", topK);
        
        _indexLock.Wait();
        try
        {
            using var queryDescriptor = DescriptorHandle.Deserialize(descriptor);
            var results = _index.QueryVotes(queryDescriptor, topK);
            
            _logger?.LogDebug("Vote query returned {Count} candidates", results.Length);
            return results;
        }
        finally
        {
            _indexLock.Release();
        }
    }
    
    /// <inheritdoc/>
    public VerificationResult Verify(byte[] queryDescriptor, byte[] candidateDescriptor)
    {
        ThrowIfDisposed();
        
        if (queryDescriptor == null || queryDescriptor.Length == 0)
            throw new ArgumentException("Query descriptor cannot be null or empty", nameof(queryDescriptor));
        if (candidateDescriptor == null || candidateDescriptor.Length == 0)
            throw new ArgumentException("Candidate descriptor cannot be null or empty", nameof(candidateDescriptor));
        
        using var queryHandle = DescriptorHandle.Deserialize(queryDescriptor);
        using var candidateHandle = DescriptorHandle.Deserialize(candidateDescriptor);
        
        var result = IndexHandle.Verify(queryHandle, candidateHandle);
        
        _logger?.LogDebug("Verification: RANSAC={Inliers}/{Corr} ({Ratio:P0}), DenseICP={Fitness:F4}, Final={Score:F1}%",
            result.RansacInliers, result.Correspondences, result.RansacInlierRatio,
            result.IcpFitnessScore, result.FinalScore);
        
        return result;
    }
    
    /// <inheritdoc/>
    public void AddToIndex(long id, byte[] descriptor)
    {
        ThrowIfDisposed();
        
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _indexLock.Wait();
        try
        {
            using var desc = DescriptorHandle.Deserialize(descriptor);
            _index.Add(desc, id);
            _indexCount++;
            
            _logger?.LogDebug("Added unit {Id} to index (total units: {Count})", id, _indexCount);
        }
        finally
        {
            _indexLock.Release();
        }
    }
    
    /// <inheritdoc/>
    public void ClearIndex()
    {
        ThrowIfDisposed();
        
        _indexLock.Wait();
        try
        {
            _index?.Dispose();
            _index = IndexHandle.Create();
            _indexCount = 0;
            _logger?.LogInformation("Index cleared and recreated");
        }
        finally
        {
            _indexLock.Release();
        }
    }
    
    /// <inheritdoc/>
    public void TrainIndex()
    {
        ThrowIfDisposed();
        
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _logger?.LogInformation("Training IVF index with {Count} units' keypoints", _indexCount);
        _index.Train();
        _logger?.LogInformation("IVF index training complete");
    }
    
    /// <inheritdoc/>
    public void SaveIndex(string path)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _logger?.LogInformation("Saving index to {Path}", path);
        _index.Save(path);
        _logger?.LogInformation("Index saved successfully");
    }
    
    /// <inheritdoc/>
    public void LoadIndex(string path)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or empty", nameof(path));
        
        _logger?.LogInformation("Loading index from {Path}", path);
        
        // Dispose old index if exists
        _index?.Dispose();
        _index = IndexHandle.Load(path);
        
        // Note: We don't know the count after loading, this would need
        // an additional native function to query index size
        _indexCount = 0; // Reset, will be inaccurate until we add a size function
        
        _logger?.LogInformation("Index loaded successfully");
    }
    
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FingerprintEngine));
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        _index?.Dispose();
        _index = null;
        
        _indexLock.Dispose();
        _disposed = true;
        
        _logger?.LogInformation("FingerprintEngine disposed");
    }
}
