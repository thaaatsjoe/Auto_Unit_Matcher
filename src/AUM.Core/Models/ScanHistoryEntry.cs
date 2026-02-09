namespace AUM.Core.Models;

/// <summary>
/// Record of a scan operation, used for history and duplicate detection.
/// Retained for 1.5 years per PRD.
/// </summary>
public class ScanHistoryEntry
{
    public long Id { get; set; }

    /// <summary>When the scan was performed.</summary>
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Employee who performed the scan.</summary>
    public required string EmployeeNumber { get; set; }

    /// <summary>Name of employee (denormalized for reports).</summary>
    public required string EmployeeName { get; set; }

    /// <summary>Which workstation.</summary>
    public required string StationId { get; set; }

    /// <summary>Which scanner (1 or 2).</summary>
    public required string ScannerId { get; set; }

    /// <summary>Matched unit ID, null if no match found.</summary>
    public long? MatchedUnitId { get; set; }

    /// <summary>Match confidence score (0-100).</summary>
    public float? Confidence { get; set; }

    /// <summary>Whether operator confirmed the match.</summary>
    public bool? Confirmed { get; set; }

    /// <summary>Detected as duplicate scan within 5 hours.</summary>
    public bool IsDuplicate { get; set; }

    /// <summary>Was this a reprint operation (not a new scan).</summary>
    public bool IsReprint { get; set; }

    /// <summary>Optional operator notes.</summary>
    public string? OperatorNotes { get; set; }
}
