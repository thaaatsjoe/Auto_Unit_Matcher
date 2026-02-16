using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using AUM.Core.Models;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for managing fingerprint units.
/// </summary>
public class UnitService : IUnitService
{
    private readonly IUnitRepository _unitRepository;
    private readonly IFingerprintEngine _engine;
    private readonly IIndexService _indexService;
    private readonly ILogger<UnitService>? _logger;
    
    public UnitService(
        IUnitRepository unitRepository,
        IFingerprintEngine engine,
        IIndexService indexService,
        ILogger<UnitService>? logger = null)
    {
        _unitRepository = unitRepository;
        _engine = engine;
        _indexService = indexService;
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public async Task<long> RegisterUnitAsync(string stlPath, string caseId)
    {
        if (string.IsNullOrWhiteSpace(stlPath))
            throw new ArgumentException("STL path cannot be null or empty", nameof(stlPath));
        if (string.IsNullOrWhiteSpace(caseId))
            throw new ArgumentException("Case ID cannot be null or empty", nameof(caseId));
        
        _logger?.LogInformation("Registering unit from {Path} for case {CaseId}", stlPath, caseId);
        
        // Extract descriptor from STL
        byte[] descriptor;
        try
        {
            descriptor = _engine.ExtractDescriptor(stlPath);
        }
        catch (EngineException ex)
        {
            _logger?.LogError(ex, "Failed to extract descriptor from {Path}", stlPath);
            throw;
        }
        
        // Create unit record
        var unit = new Unit
        {
            CaseId = caseId,
            StlPath = stlPath,
            DescriptorBlob = descriptor,
            CreatedAt = DateTime.UtcNow
        };
        
        // Save to database (upsert handles the case where STL was previously registered
        // but needs re-extraction due to a failed or missing descriptor)
        var id = await _unitRepository.UpsertByStlPathAsync(unit);
        _logger?.LogInformation("Registered unit {Id} for case {CaseId}", id, caseId);
        
        // Add to search index
        await _indexService.AddAsync(id, descriptor);
        
        return id;
    }
    
    /// <inheritdoc/>
    public async Task<Unit?> GetByIdAsync(long id)
    {
        return await _unitRepository.GetByIdAsync(id);
    }
    
    /// <inheritdoc/>
    public async Task<Unit?> GetByStlPathAsync(string stlPath)
    {
        return await _unitRepository.GetByStlPathAsync(stlPath);
    }
    
    /// <inheritdoc/>
    public async Task<IEnumerable<Unit>> GetByCaseIdAsync(string caseId)
    {
        return await _unitRepository.GetByCaseIdAsync(caseId);
    }
    
    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string stlPath)
    {
        return await _unitRepository.ExistsAsync(stlPath);
    }
}
