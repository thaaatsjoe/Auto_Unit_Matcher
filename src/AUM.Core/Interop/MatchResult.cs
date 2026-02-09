using System.Runtime.InteropServices;

namespace AUM.Core.Interop;

/// <summary>
/// Result of a similarity query against the fingerprint index.
/// Maps to AUM_MatchResult in exports.h.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MatchResult
{
    /// <summary>Database ID of the matched unit.</summary>
    public long Id;
    
    /// <summary>Distance score (lower = better match).</summary>
    public float Distance;
    
    /// <summary>Confidence score as percentage (0-100).</summary>
    public float Confidence;
    
    /// <summary>
    /// Returns a string representation of the match result.
    /// </summary>
    public override string ToString()
    {
        return $"MatchResult {{ Id={Id}, Distance={Distance:F4}, Confidence={Confidence:F1}% }}";
    }
}
