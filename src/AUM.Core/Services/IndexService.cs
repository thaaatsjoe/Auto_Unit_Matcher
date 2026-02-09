using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for managing the FAISS search index.
/// </summary>
public class IndexService : IIndexService
{
    private readonly IFingerprintEngine _engine;
    private readonly IUnitRepository _unitRepository;
    private readonly string _indexPath;
    private readonly ILogger<IndexService>? _logger;
    
    private int _count;
    private bool _isReady;
    
    public IndexService(
        IFingerprintEngine engine,
        IUnitRepository unitRepository,
        string indexPath,
        ILogger<IndexService>? logger = null)
    {
        _engine = engine;
        _unitRepository = unitRepository;
        _indexPath = indexPath;
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public int Count => _count;
    
    /// <inheritdoc/>
    public bool IsReady => _isReady;
    
    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("Initializing index service");
        
        // Try to load existing index
        if (File.Exists(_indexPath))
        {
            try
            {
                _engine.LoadIndex(_indexPath);
                _isReady = true;
                _logger?.LogInformation("Loaded existing index from {Path}", _indexPath);
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load index from {Path}, rebuilding", _indexPath);
            }
        }
        
        // Rebuild from database
        await RebuildAsync();
    }
    
    /// <inheritdoc/>
    public Task AddAsync(long unitId, byte[] descriptor)
    {
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        
        _engine.AddToIndex(unitId, descriptor);
        _count++;
        
        _logger?.LogDebug("Added unit {Id} to index (total: {Count})", unitId, _count);
        
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public async Task RebuildAsync()
    {
        _logger?.LogInformation("Rebuilding index from database");
        
        _isReady = false;
        _count = 0;
        
        // Load all descriptors from database
        var descriptors = await _unitRepository.GetAllDescriptorsAsync();
        
        foreach (var (id, blob) in descriptors)
        {
            _engine.AddToIndex(id, blob);
            _count++;
        }
        
        _logger?.LogInformation("Added {Count} entries to index", _count);
        
        // Train and mark ready
        await TrainAsync();
    }
    
    /// <inheritdoc/>
    public Task TrainAsync()
    {
        if (_count > 0)
        {
            _logger?.LogInformation("Training index with {Count} entries", _count);
            _engine.TrainIndex();
        }
        
        _isReady = true;
        _logger?.LogInformation("Index is ready");
        
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public Task SaveAsync()
    {
        _logger?.LogInformation("Saving index to {Path}", _indexPath);
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(_indexPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        _engine.SaveIndex(_indexPath);
        _logger?.LogInformation("Index saved successfully");
        
        return Task.CompletedTask;
    }
}
