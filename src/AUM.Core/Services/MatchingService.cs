using AUM.Core.Data.Repositories;
using AUM.Core.Engine;
using AUM.Core.Interop;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

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
    private readonly OnnxInferenceService? _onnxService;
    
    public MatchingService(
        IFingerprintEngine engine,
        IUnitRepository unitRepository,
        ILogger<MatchingService>? logger = null,
        OnnxInferenceService? onnxService = null)
    {
        _engine = engine;
        _unitRepository = unitRepository;
        _logger = logger;
        _onnxService = onnxService;
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
        const int voteCandidates = 50;
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
            
            // EARLY EXIT: Dense ICP is a near-perfect geometric discriminator.
            // Once we find a microscopic lock (>=90%), there is zero physical
            // probability of finding a better match. Stop processing remaining
            // candidates to avoid wasting 10-30s of Dense ICP compute.
            if (finalScore >= 90.0f)
            {
                _logger?.LogInformation("Early exit: {CaseId} scored {Score:F1}% — skipping remaining candidates",
                    unit.CaseId, finalScore);
                break;
            }
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

    // ========================================================================
    // V2 ML PIPELINE PLUMBING TEST
    // ========================================================================
    public string TestV2MlPipeline(string testStlPath)
    {
        var sb = new System.Text.StringBuilder();

        if (_onnxService == null)
        {
            var msg = "ERROR: OnnxInferenceService is not injected.";
            _logger?.LogWarning(msg);
            return msg;
        }

        try
        {
            sb.AppendLine($"[V2 ML TEST] Starting ONNX Pipeline Test on:\n{testStlPath}\n");

            // 1. Load STL points
            float[,] points = ParseStlVertices(testStlPath);
            int numPoints = points.GetLength(0);
            sb.AppendLine($"[STAGE 0] Parsed {numPoints} physical vertices from STL byte buffer.");

            // 2. Stage 1: Backbone Feature Extraction
            sb.AppendLine("[STAGE 1] Executing Backbone Feature Extraction (aum_backbone.onnx)...");
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            float[] features = _onnxService.ExtractFeatures(points);
            stopwatch.Stop();
            
            int featureCount = features.Length / 128;
            sb.AppendLine($"          -> SUCCESS! Output Tensor Shape: [{featureCount}, 128]");
            sb.AppendLine($"          -> Inference Time: {stopwatch.ElapsedMilliseconds} ms\n");

            // 3. Stage 2: Matcher Verification
            sb.AppendLine("[STAGE 2] Executing Matcher Cross-Attention (aum_matcher.onnx)...");
            
            // Clone source features to simulate a target hit
            float[] targetFeatures = (float[])features.Clone();
            
            stopwatch.Restart();
            var (sourceDesc, targetDesc) = _onnxService.VerifyMatch(features, featureCount, targetFeatures, featureCount);
            stopwatch.Stop();
            
            sb.AppendLine($"          -> SUCCESS! Source Desc Length: {sourceDesc.Length}, Target Desc: {targetDesc.Length}");
            sb.AppendLine($"          -> Inference Time: {stopwatch.ElapsedMilliseconds} ms\n");
            
            sb.AppendLine("=========== V2 ML PIPELINE ONNX INTEGRATION TEST PASSED! ===========");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"\n[FATAL ERROR] V2 ML Pipeline ONNX Test Failed:\n{ex.Message}\n{ex.StackTrace}");
            _logger?.LogError(ex, "V2 ML Pipeline ONNX Test Failed.");
        }

        return sb.ToString();
    }

    private float[,] ParseStlVertices(string stlPath)
    {
        // Extremely lightweight binary STL parser to feed the ONNX tensor
        // MODIFICATION: Use a HashSet to deduplicate identical vertices, as 
        // STL files naively define 3 discrete vertices per triangle regardless of connectivity.
        using var stream = File.OpenRead(stlPath);
        using var reader = new BinaryReader(stream);
        
        reader.ReadBytes(80); // Skip header
        uint numTriangles = reader.ReadUInt32();
        
        var uniqueVertices = new HashSet<(float x, float y, float z)>();
        
        for (uint i = 0; i < numTriangles; i++)
        {
            reader.ReadSingle(); reader.ReadSingle(); reader.ReadSingle(); // Skip Normal
            
            for (int j = 0; j < 3; j++)
            {
                float x = reader.ReadSingle();
                float y = reader.ReadSingle();
                float z = reader.ReadSingle();
                
                // HashSet automatically ignores duplicates in O(1) time
                uniqueVertices.Add((x, y, z));
            }
            reader.ReadUInt16(); // Skip attribute byte count
        }
        
        // Convert Deduplicated HashSet back to flat 2D Array for Neural ingestion
        float[,] points = new float[uniqueVertices.Count, 3];
        int ptIdx = 0;
        foreach (var v in uniqueVertices)
        {
            points[ptIdx, 0] = v.x;
            points[ptIdx, 1] = v.y;
            points[ptIdx, 2] = v.z;
            ptIdx++;
        }
        
        return points;
    }
}
