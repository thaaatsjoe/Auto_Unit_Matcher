using AUM.Core.Interop;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Engine;

/// <summary>
/// High-level wrapper for the native fingerprint engine.
/// Provides managed API with automatic resource cleanup.
/// </summary>
public sealed class FingerprintEngine : IFingerprintEngine
{
    private readonly ILogger<FingerprintEngine>? _logger;
    private IndexHandle? _index;
    private int _indexCount;
    private bool _disposed;
    
    /// <summary>
    /// Creates a new FingerprintEngine with an empty index.
    /// </summary>
    public FingerprintEngine(ILogger<FingerprintEngine>? logger = null)
    {
        _logger = logger;
        _index = IndexHandle.Create();
        _indexCount = 0;
        _logger?.LogInformation("FingerprintEngine initialized with version {Version}", Version);
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
        
        _logger?.LogDebug("Extracting descriptor from {Path}", stlPath);
        
        // Parse STL file
        var parseResult = NativeMethods.aum_parse_stl(stlPath, out var pointCloudPtr);
        EngineException.ThrowIfError(parseResult);
        
        using var pointCloud = new PointCloudHandle(pointCloudPtr);
        _logger?.LogDebug("Parsed STL with {Count} points", pointCloud.PointCount);
        
        // Extract descriptors
        var extractResult = NativeMethods.aum_extract_descriptors(pointCloud.DangerousGetHandle(), out var descriptorPtr);
        EngineException.ThrowIfError(extractResult);
        
        using var descriptor = new DescriptorHandle(descriptorPtr);
        
        // Serialize to blob
        var blob = descriptor.Serialize();
        _logger?.LogDebug("Extracted descriptor blob of {Size} bytes", blob.Length);
        
        return blob;
    }
    
    /// <inheritdoc/>
    public MatchResult[] Query(byte[] descriptor, int topK = 5)
    {
        ThrowIfDisposed();
        
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _logger?.LogDebug("Querying index for top {K} matches", topK);
        
        using var queryDescriptor = DescriptorHandle.Deserialize(descriptor);
        var results = _index.Query(queryDescriptor, topK);
        
        _logger?.LogDebug("Query returned {Count} results", results.Length);
        return results;
    }
    
    /// <inheritdoc/>
    public void AddToIndex(long id, byte[] descriptor)
    {
        ThrowIfDisposed();
        
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        using var desc = DescriptorHandle.Deserialize(descriptor);
        _index.Add(desc, id);
        _indexCount++;
        
        _logger?.LogDebug("Added entry {Id} to index (total: {Count})", id, _indexCount);
    }
    
    /// <inheritdoc/>
    public void TrainIndex()
    {
        ThrowIfDisposed();
        
        if (_index == null || _index.IsInvalid)
            throw new InvalidOperationException("Index is not initialized");
        
        _logger?.LogInformation("Training index with {Count} entries", _indexCount);
        _index.Train();
        _logger?.LogInformation("Index training complete");
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
        _disposed = true;
        
        _logger?.LogInformation("FingerprintEngine disposed");
    }
}
