namespace AUM.Core.Models;

/// <summary>
/// Tracks STL files pending descriptor extraction.
/// </summary>
public class ExtractionQueueEntry
{
    public long Id { get; set; }

    /// <summary>Full path to the STL file.</summary>
    public required string StlPath { get; set; }

    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>When the file was created (for priority sorting).</summary>
    public DateTime FileCreatedAt { get; set; }

    /// <summary>Current status: PENDING, IN_PROGRESS, COMPLETE, FAILED.</summary>
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Pending;

    /// <summary>Error message if status is FAILED.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>When extraction started.</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>When extraction completed.</summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Status of an extraction queue entry.
/// </summary>
public enum ExtractionStatus
{
    Pending,
    InProgress,
    Complete,
    Failed
}
