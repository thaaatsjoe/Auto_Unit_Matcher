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
        
        // ================================================================
        // Stage 1: FAISS Shortlist (fast, ~50ms)
        // Get more candidates than needed for re-ranking in Stage 2
        // ================================================================
        const int shortlistSize = 20;
        _logger?.LogInformation("Stage 1: FAISS shortlist for top {K} candidates", shortlistSize);
        
        var engineResults = _engine.Query(descriptor, shortlistSize);
        
        if (engineResults.Length == 0)
        {
            _logger?.LogInformation("No matches found");
            return Array.Empty<MatchingResult>();
        }
        
        _logger?.LogInformation("Stage 1 returned {Count} candidates", engineResults.Length);
        
        // ================================================================
        // Stage 2: Point-to-Point Comparison (accurate, partial-robust)
        // For each candidate, compare individual FPFH features
        // ================================================================
        _logger?.LogInformation("Stage 2: Point-to-point comparison for {Count} candidates", engineResults.Length);
        
        var candidates = new List<MatchingResult>();
        
        foreach (var match in engineResults)
        {
            var unit = await _unitRepository.GetByIdAsync(match.Id);
            
            if (unit != null)
            {
                // Point-to-point comparison using stored descriptor blobs
                float pointScore;
                try
                {
                    pointScore = _engine.CompareDescriptors(descriptor, unit.DescriptorBlob);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Point-to-point comparison failed for unit {Id}, using FAISS score", match.Id);
                    pointScore = match.Confidence; // Fallback to FAISS score
                }
                
                _logger?.LogDebug(
                    "  Unit {Id} ({CaseId}): FAISS={FaissScore:F1}%, Point-to-Point={PtpScore:F1}%",
                    match.Id, unit.CaseId, match.Confidence, pointScore);
                
                candidates.Add(new MatchingResult
                {
                    UnitId = match.Id,
                    CaseId = unit.CaseId,
                    StlPath = unit.StlPath,
                    Distance = match.Distance,
                    Confidence = pointScore, // Use point-to-point score
                    Rank = 0 // Will be assigned after re-ranking
                });
            }
            else
            {
                _logger?.LogWarning("Unit {Id} not found in database", match.Id);
            }
        }
        
        // ================================================================
        // Re-rank by point-to-point score (descending) and take top K
        // ================================================================
        var finalResults = candidates
            .OrderByDescending(r => r.Confidence)
            .Take(topK)
            .ToList();
        
        // Assign final ranks
        for (int i = 0; i < finalResults.Count; i++)
        {
            finalResults[i].Rank = i + 1;
        }
        
        _logger?.LogInformation("Stage 2 complete. Top match: {CaseId} at {Score:F1}%",
            finalResults.FirstOrDefault()?.CaseId ?? "none",
            finalResults.FirstOrDefault()?.Confidence ?? 0);
        
        return finalResults.ToArray();
    }
    
    /// <summary>
    /// Recalculates confidence scores using gap-based scoring.
    /// Instead of raw exponential decay (which clusters dental crowns at 99%+),
    /// this scores based on how separated each result is relative to the best and worst.
    /// A quality gate penalizes all scores if even the best match is poor.
    /// </summary>
    private void RecalculateConfidenceScores(List<MatchingResult> results)
    {
        if (results.Count == 0) return;
        
        // Single result — use quality gate only
        if (results.Count == 1)
        {
            results[0].Confidence = CalculateQualityGate(results[0].Distance);
            _logger?.LogDebug("Single result: distance={Distance:F6}, confidence={Confidence:F1}%",
                results[0].Distance, results[0].Confidence);
            return;
        }
        
        float bestDistance = results[0].Distance;
        float worstDistance = results[results.Count - 1].Distance;
        float distanceRange = worstDistance - bestDistance;
        
        // Quality gate: penalizes all scores if the best match itself is poor.
        // Scale of 50 means: distance 0 → 100%, distance 0.01 → 60%, distance 0.05 → 8%
        float qualityGate = CalculateQualityGate(bestDistance);
        
        _logger?.LogInformation(
            "Gap scoring: bestDist={Best:F6}, worstDist={Worst:F6}, range={Range:F6}, qualityGate={Gate:F1}%",
            bestDistance, worstDistance, distanceRange, qualityGate);
        
        for (int i = 0; i < results.Count; i++)
        {
            float gapScore;
            
            if (distanceRange < 1e-8f)
            {
                // All distances are effectively identical — flat distribution
                gapScore = 100.0f;
            }
            else
            {
                // Linear interpolation: best=100%, worst=0%
                gapScore = 100.0f * (1.0f - (results[i].Distance - bestDistance) / distanceRange);
            }
            
            // Apply quality gate
            float finalScore = qualityGate * gapScore / 100.0f;
            finalScore = Math.Max(0.0f, Math.Min(100.0f, finalScore));
            
            _logger?.LogDebug(
                "  Rank {Rank}: distance={Distance:F6}, gapScore={Gap:F1}%, final={Final:F1}%",
                results[i].Rank, results[i].Distance, gapScore, finalScore);
            
            results[i].Confidence = finalScore;
        }
    }
    
    /// <summary>
    /// Quality gate: how good is the best match in absolute terms?
    /// Uses a tuned exponential decay so that only very close matches score high.
    /// </summary>
    private static float CalculateQualityGate(float bestDistance)
    {
        // Scale factor tuned for L2-normalized FPFH descriptors.
        // Typical distances between dental crowns: 0.001 - 0.02
        // scale=50: distance 0.0 → 100%, 0.005 → 78%, 0.01 → 61%, 0.02 → 37%, 0.05 → 8%
        const float scale = 50.0f;
        return 100.0f * (float)Math.Exp(-bestDistance * scale);
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
