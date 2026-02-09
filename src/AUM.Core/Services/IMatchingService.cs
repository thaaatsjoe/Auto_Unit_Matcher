using AUM.Core.Interop;
using AUM.Core.Models;

namespace AUM.Core.Services;

/// <summary>
/// Result of a fingerprint matching query with enriched unit details.
/// </summary>
public class MatchingResult
{
    /// <summary>Database ID of the matched unit.</summary>
    public long UnitId { get; set; }
    
    /// <summary>Case ID of the matched unit.</summary>
    public string CaseId { get; set; } = string.Empty;
    
    /// <summary>Path to the original STL file.</summary>
    public string StlPath { get; set; } = string.Empty;
    
    /// <summary>Distance score (lower = better match).</summary>
    public float Distance { get; set; }
    
    /// <summary>Confidence score as percentage (0-100).</summary>
    public float Confidence { get; set; }
    
    /// <summary>Rank in the results (1 = best match).</summary>
    public int Rank { get; set; }
}

/// <summary>
/// Service for querying fingerprint matches.
/// </summary>
public interface IMatchingService
{
    /// <summary>
    /// Finds matches for a serialized descriptor.
    /// </summary>
    /// <param name="descriptor">Serialized descriptor blob.</param>
    /// <param name="topK">Number of results to return (default 5 per PRD).</param>
    /// <returns>Array of matches with unit details, ordered by confidence.</returns>
    Task<MatchingResult[]> FindMatchesAsync(byte[] descriptor, int topK = 5);
    
    /// <summary>
    /// Extracts a descriptor from an STL file and finds matches.
    /// </summary>
    /// <param name="stlPath">Path to the scanned STL file.</param>
    /// <param name="topK">Number of results to return (default 5 per PRD).</param>
    /// <returns>Array of matches with unit details, ordered by confidence.</returns>
    Task<MatchingResult[]> FindMatchesFromStlAsync(string stlPath, int topK = 5);
}
