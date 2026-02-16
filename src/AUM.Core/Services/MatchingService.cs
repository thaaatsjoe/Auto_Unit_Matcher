using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using AUM.Core.Interop;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for querying fingerprint matches.
/// Two-stage pipeline:
///   Stage 1: FAISS local feature voting (ISS+SHOT352 keypoints)
///   Stage 2: RANSAC + Dense Point-to-Plane ICP geometric verification
/// Confidence score = ICP-verified alignment fitness.
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
        // Stage 1: FAISS Voting (fast, ~100ms for 6000 units)
        // Each query keypoint votes for its nearest unit(s)
        // ================================================================
        const int voteCandidates = 30;
        _logger?.LogInformation("Stage 1: FAISS voting for top {K} candidates", voteCandidates);
        
        VoteResult[] voteResults;
        try
        {
            voteResults = _engine.QueryVotes(descriptor, voteCandidates);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Stage 1 voting failed");
            return Array.Empty<MatchingResult>();
        }
        
        if (voteResults.Length == 0)
        {
            _logger?.LogInformation("No vote results found");
            return Array.Empty<MatchingResult>();
        }
        
        _logger?.LogInformation("Stage 1 returned {Count} candidates", voteResults.Length);
        for (int i = 0; i < voteResults.Length; i++)
        {
            _logger?.LogInformation("  Vote #{Rank}: UnitId={Id} Score={Score:F2} Votes={Count}",
                i + 1, voteResults[i].UnitId, voteResults[i].VoteScore, voteResults[i].VoteCount);
        }
        
        // ================================================================
        // Stage 2: RANSAC + Dense Point-to-Plane ICP Verification
        // For each vote candidate, verify spatial alignment
        // ================================================================
        _logger?.LogInformation("Stage 2: RANSAC+DenseICP verification for {Count} candidates", voteResults.Length);
        
        var candidates = new List<MatchingResult>();
        
        foreach (var vote in voteResults)
        {
            var unit = await _unitRepository.GetByIdAsync(vote.UnitId);
            
            if (unit == null)
            {
                _logger?.LogWarning("Unit {Id} not found in database", vote.UnitId);
                continue;
            }
            
            // Run geometric verification
            float finalScore;
            try
            {
                var verification = _engine.Verify(descriptor, unit.DescriptorBlob);
                finalScore = verification.FinalScore;
                
                _logger?.LogInformation(
                    "  VERIFY {CaseId}: RANSAC={Inliers}/{Corr} ({Ratio:P0}), " +
                    "DenseICP fitness={Fitness:F4}, Final={Score:F1}%, Votes={Votes}",
                    unit.CaseId,
                    verification.RansacInliers, verification.Correspondences, verification.RansacInlierRatio,
                    verification.IcpFitnessScore, verification.FinalScore, vote.VoteCount);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Verification failed for unit {Id}, using vote score as fallback", vote.UnitId);
                // Fallback: normalize vote score to rough confidence
                finalScore = Math.Min(100f, vote.VoteScore * 10f);
            }
            
            candidates.Add(new MatchingResult
            {
                UnitId = vote.UnitId,
                CaseId = unit.CaseId,
                StlPath = unit.StlPath,
                Distance = finalScore,
                Confidence = finalScore,
                Rank = 0
            });
        }
        
        // ================================================================
        // Rank by final verified score (descending) and take top K
        // ================================================================
        var finalResults = candidates
            .OrderByDescending(r => r.Confidence)
            .Take(topK)
            .ToList();
        
        for (int i = 0; i < finalResults.Count; i++)
        {
            finalResults[i].Rank = i + 1;
        }
        
        _logger?.LogInformation("Pipeline complete. Top match: {CaseId} at {Score:F1}%",
            finalResults.FirstOrDefault()?.CaseId ?? "none",
            finalResults.FirstOrDefault()?.Confidence ?? 0);
        
        return finalResults.ToArray();
    }
    
    /// <inheritdoc/>
    public async Task<MatchingResult[]> FindMatchesFromStlAsync(string stlPath, int topK = 5)
    {
        if (string.IsNullOrWhiteSpace(stlPath))
            throw new ArgumentException("STL path cannot be null or empty", nameof(stlPath));
        
        _logger?.LogInformation("Extracting ISS+SHOT352+DenseCloud descriptor from {Path}", stlPath);
        
        var descriptor = _engine.ExtractDescriptor(stlPath);
        
        return await FindMatchesAsync(descriptor, topK);
    }
}
