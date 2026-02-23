using System.Text.Json;
using System.Text.Json.Serialization;
using AUM.Core.Engine;
using AUM.Core.Interop;

namespace AUM.Core.Services;

/// <summary>
/// Headless ML-tuning runner. Executes the full pipeline without WPF:
///   1. Set extraction config from JSON
///   2. Extract descriptors + build FAISS index for all C&B STLs in db-folder
///   3. Match every scan in query-folder
///   4. Write results.json
///   5. Exit cleanly
/// </summary>
public static class HeadlessTuneRunner
{
    /// <summary>CLI arguments for --tune mode.</summary>
    public class TuneArgs
    {
        public string ConfigPath { get; set; } = "";
        public string DbFolder { get; set; } = "";
        public string QueryFolder { get; set; } = "";
        public string OutPath { get; set; } = "";
    }
    
    /// <summary>Config JSON schema matching optimize.py output.</summary>
    public class TuneConfig
    {
        [JsonPropertyName("VoxelSize")]
        public float VoxelSize { get; set; } = 0.15f;
        
        [JsonPropertyName("KeypointVoxelSize")]
        public float KeypointVoxelSize { get; set; } = 0.8f;
        
        [JsonPropertyName("ShotRadius")]
        public float ShotRadius { get; set; } = 1.0f;
        
        [JsonPropertyName("IcpFitnessDecay")]
        public float IcpFitnessDecay { get; set; } = 0.5f;
    }
    
    /// <summary>Single result entry in results.json.</summary>
    public class TuneResult
    {
        [JsonPropertyName("Query")]
        public string Query { get; set; } = "";
        
        [JsonPropertyName("MatchedCase")]
        public string MatchedCase { get; set; } = "";
        
        [JsonPropertyName("Score")]
        public float Score { get; set; }
    }
    
    /// <summary>
    /// Parse --tune CLI arguments from the command-line args array.
    /// Returns null if --tune is not present.
    /// </summary>
    public static TuneArgs? ParseArgs(string[] args)
    {
        var argList = args.ToList();
        int tuneIdx = argList.IndexOf("--tune");
        if (tuneIdx < 0) return null;
        
        var result = new TuneArgs();
        for (int i = tuneIdx + 1; i < argList.Count; i++)
        {
            switch (argList[i])
            {
                case "--config":
                    if (i + 1 < argList.Count) result.ConfigPath = argList[++i];
                    break;
                case "--db-folder":
                    if (i + 1 < argList.Count) result.DbFolder = argList[++i];
                    break;
                case "--query-folder":
                    if (i + 1 < argList.Count) result.QueryFolder = argList[++i];
                    break;
                case "--out":
                    if (i + 1 < argList.Count) result.OutPath = argList[++i];
                    break;
            }
        }
        
        return result;
    }
    
    private static void Info(string msg) => Console.WriteLine($"[TUNE] {msg}");
    private static void Warn(string msg) => Console.WriteLine($"[TUNE WARN] {msg}");
    private static void Err(string msg) => Console.Error.WriteLine($"[TUNE ERROR] {msg}");
    
    /// <summary>
    /// Run the headless tuning pipeline. Returns exit code (0 = success).
    /// </summary>
    public static async Task<int> RunAsync(TuneArgs args)
    {
        Info("=== HEADLESS TUNE MODE ===");
        
        // Validate arguments
        if (string.IsNullOrEmpty(args.ConfigPath) || !File.Exists(args.ConfigPath))
        {
            Err($"Config file not found: {args.ConfigPath}");
            return 1;
        }
        if (string.IsNullOrEmpty(args.DbFolder) || !Directory.Exists(args.DbFolder))
        {
            Err($"DB folder not found: {args.DbFolder}");
            return 1;
        }
        if (string.IsNullOrEmpty(args.QueryFolder) || !Directory.Exists(args.QueryFolder))
        {
            Err($"Query folder not found: {args.QueryFolder}");
            return 1;
        }
        if (string.IsNullOrEmpty(args.OutPath))
        {
            Err("Output path not specified");
            return 1;
        }
        
        try
        {
            // Step 1: Load config
            var configJson = await File.ReadAllTextAsync(args.ConfigPath);
            var config = JsonSerializer.Deserialize<TuneConfig>(configJson) ?? new TuneConfig();
            Info($"Config: Voxel={config.VoxelSize}, Keypoint={config.KeypointVoxelSize}, SHOT={config.ShotRadius}, Decay={config.IcpFitnessDecay}");
            
            // Step 2: Create engine and set config
            using var engine = new FingerprintEngine();
            var descConfig = new DescriptorConfig
            {
                VoxelSize = config.VoxelSize,
                KeypointVoxelSize = config.KeypointVoxelSize,
                ShotRadius = config.ShotRadius,
                IcpFitnessDecay = config.IcpFitnessDecay
            };
            engine.SetConfig(descConfig);
            
            // Step 3: Scan, extract, and index all C&B STLs in db-folder
            var dbFiles = Directory.GetFiles(args.DbFolder, "*.stl", SearchOption.AllDirectories);
            Info($"Found {dbFiles.Length} STL files in DB folder");
            
            // Map unitId -> (caseId, descriptor blob) for result lookup and Stage 2 cache
            var unitMap = new Dictionary<long, (string CaseId, byte[] Blob)>();
            long nextUnitId = 1;
            int skipped = 0;
            int indexed = 0;
            
            foreach (var stlPath in dbFiles)
            {
                // Apply C&B exclusion filter
                if (!StlMonitorService.IsCBFile(stlPath))
                {
                    skipped++;
                    continue;
                }
                
                try
                {
                    // Use filename (without extension) as the unit identity.
                    // DB files are flat in folder — no subfolders.
                    var caseId = Path.GetFileNameWithoutExtension(stlPath);
                    var unitId = nextUnitId++;
                    
                    var descriptor = engine.ExtractDescriptor(stlPath);
                    engine.AddToIndex(unitId, descriptor);
                    
                    // Cache the descriptor blob for Stage 2 verification.
                    // Avoids re-extracting from disk during matching (~5min/query → ~5sec).
                    unitMap[unitId] = (caseId, descriptor);
                    indexed++;
                    
                    Info($"  Indexed: {Path.GetFileName(stlPath)} → Unit {unitId}, Case {caseId}");
                }
                catch (Exception ex)
                {
                    Warn($"Failed to process {Path.GetFileName(stlPath)}: {ex.Message}");
                }
            }
            
            Info($"Indexed {indexed} C&B units, skipped {skipped} non-C&B files");
            
            if (indexed == 0)
            {
                Err("No units indexed — cannot run matching");
                return 1;
            }
            
            // Step 4: Train the index
            engine.TrainIndex();
            Info($"FAISS index trained with {indexed} units");
            
            // Step 5: Match each query scan
            var queryFiles = Directory.GetFiles(args.QueryFolder, "*.stl", SearchOption.AllDirectories);
            Info($"Found {queryFiles.Length} query scans");
            
            var results = new List<TuneResult>();
            const int voteCandidates = 10; // Top-10 is sufficient for parameter tuning
            
            foreach (var queryPath in queryFiles)
            {
                var queryName = Path.GetFileName(queryPath);
                Info($"Matching: {queryName}");
                
                try
                {
                    // Extract query descriptor
                    var queryDescriptor = engine.ExtractDescriptor(queryPath);
                    
                    // Stage 1: FAISS voting
                    var votes = engine.QueryVotes(queryDescriptor, voteCandidates);
                    
                    if (votes.Length == 0)
                    {
                        results.Add(new TuneResult { Query = queryName, MatchedCase = "NO_MATCH", Score = 0 });
                        continue;
                    }
                    
                    // Stage 2: Verify top candidates using CACHED descriptors
                    string bestCaseId = "NO_MATCH";
                    float bestScore = 0f;
                    
                    foreach (var vote in votes)
                    {
                        if (!unitMap.TryGetValue(vote.UnitId, out var unitInfo))
                            continue;
                        
                        try
                        {
                            // Use cached blob — no disk I/O or re-extraction needed
                            var verification = engine.Verify(queryDescriptor, unitInfo.Blob);
                            
                            if (verification.FinalScore > bestScore)
                            {
                                bestScore = verification.FinalScore;
                                bestCaseId = unitInfo.CaseId;
                            }
                            
                            // Early exit on near-perfect match
                            if (verification.FinalScore >= 90.0f)
                            {
                                Info($"  Early exit: {unitInfo.CaseId} scored {verification.FinalScore:F1}%");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Warn($"  Verify failed for {unitInfo.CaseId}: {ex.Message}");
                        }
                    }
                    
                    results.Add(new TuneResult
                    {
                        Query = queryName,
                        MatchedCase = bestCaseId,
                        Score = bestScore
                    });
                    
                    Info($"  Result: {queryName} → {bestCaseId} ({bestScore:F1}%)");
                }
                catch (Exception ex)
                {
                    Err($"Failed to match {queryName}: {ex.Message}");
                    results.Add(new TuneResult { Query = queryName, MatchedCase = "ERROR", Score = 0 });
                }
            }
            
            // Step 6: Write results
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var resultJson = JsonSerializer.Serialize(results, jsonOptions);
            await File.WriteAllTextAsync(args.OutPath, resultJson);
            
            Info($"Results written to {args.OutPath} ({results.Count} entries)");
            Info("=== HEADLESS TUNE COMPLETE ===");
            return 0;
        }
        catch (Exception ex)
        {
            Err($"Headless tune failed: {ex}");
            return 2;
        }
    }
}
