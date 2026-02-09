namespace AUM.Core.Models;

/// <summary>
/// Flag indicating a case is being remilled.
/// Suppresses duplicate detection alerts for flagged cases.
/// </summary>
public class RemillingFlag
{
    public long Id { get; set; }

    /// <summary>Case ID that is being remilled.</summary>
    public required string CaseId { get; set; }

    /// <summary>When the flag was set.</summary>
    public DateTime FlaggedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Employee number who set the flag.</summary>
    public required string FlaggedBy { get; set; }

    /// <summary>Optional notes about the remilling.</summary>
    public string? Notes { get; set; }
}
