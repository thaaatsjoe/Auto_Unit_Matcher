using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for querying fingerprint matches.
/// </summary>
public class MatchingService : IMatchingService
{
    private readonly IFingerprintEngine _engine;
    private readonly IUnitRepository _unitRepository;
    private readonly ILogger<MatchingService>? _logger;
    
    public MatchingService(
        IFingerprintEngine engine,
        IUnitRepository unitRepository,
        ILogger<MatchingService>? logger = null)
    {
        _engine = engine;
        _unitRepository = unitRepository;
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public async Task<MatchingResult[]> FindMatchesAsync(byte[] descriptor, int topK = 5)
    {
        if (descriptor == null || descriptor.Length == 0)
            throw new ArgumentException("Descriptor cannot be null or empty", nameof(descriptor));
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be positive");
        
        _logger?.LogInformation("Querying for top {K} matches", topK);
        
        // Query the engine
        var engineResults = _engine.Query(descriptor, topK);
        
        if (engineResults.Length == 0)
        {
            _logger?.LogInformation("No matches found");
            return Array.Empty<MatchingResult>();
        }
        
        _logger?.LogInformation("Found {Count} potential matches", engineResults.Length);
        
        // Enrich with unit details
        var results = new List<MatchingResult>();
        int rank = 1;
        
        foreach (var match in engineResults)
        {
            var unit = await _unitRepository.GetByIdAsync(match.Id);
            
            if (unit != null)
            {
                results.Add(new MatchingResult
                {
                    UnitId = match.Id,
                    CaseId = unit.CaseId,
                    StlPath = unit.StlPath,
                    Distance = match.Distance,
                    Confidence = match.Confidence,
                    Rank = rank++
                });
            }
            else
            {
                _logger?.LogWarning("Unit {Id} not found in database", match.Id);
            }
        }
        
        return results.ToArray();
    }
    
    /// <inheritdoc/>
    public async Task<MatchingResult[]> FindMatchesFromStlAsync(string stlPath, int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(stlPath))
            throw new ArgumentException("STL path cannot be null or empty", nameof(stlPath));
        
        _logger?.LogInformation("Extracting descriptor from {Path} for matching", stlPath);
        
        // Extract descriptor from scanned STL
        var descriptor = _engine.ExtractDescriptor(stlPath);
        
        // Find matches
        return await FindMatchesAsync(descriptor, topK);
    }
}
