using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for managing the FAISS search index.
/// The database is the single source of truth — the index is an ephemeral
/// acceleration structure rebuilt from the database on every startup.
/// </summary>
public class IndexService : IIndexService
{
    private readonly IFingerprintEngine _engine;
    private readonly IUnitRepository _unitRepository;
    private readonly ILogger<IndexService>? _logger;
    
    private int _count;
    private bool _isReady;
    
    public IndexService(
        IFingerprintEngine engine,
        IUnitRepository unitRepository,
        ILogger<IndexService>? logger = null)
    {
        _engine = engine;
        _unitRepository = unitRepository;
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public int Count => _count;
    
    /// <inheritdoc/>
    public bool IsReady => _isReady;
    
    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("Initializing index service — rebuilding from database (source of truth)");
        
        // Always rebuild from database. Never trust a file on disk.
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
        
        // Clear the existing index to remove any stale entries
        _engine.ClearIndex();
        
        // Load all descriptors from database
        var descriptors = await _unitRepository.GetAllDescriptorsAsync();
        
        foreach (var (id, blob) in descriptors)
        {
            _engine.AddToIndex(id, blob);
            _count++;
        }
        
        _logger?.LogInformation("Rebuilt index with {Count} entries from database", _count);
        
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
}
