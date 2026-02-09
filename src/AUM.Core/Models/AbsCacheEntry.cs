namespace AUM.Core.Models;

/// <summary>
/// Cached data from ABS API for a case.
/// Used for label printing when ABS is unavailable.
/// </summary>
public class AbsCacheEntry
{
    public long Id { get; set; }

    /// <summary>Case ID from folder name.</summary>
    public required string CaseId { get; set; }

    /// <summary>Due date from ABS.</summary>
    public string? DueDate { get; set; }

    /// <summary>Product name/description.</summary>
    public string? ProductName { get; set; }

    /// <summary>Patient name (HIPAA protected).</summary>
    public string? PatientName { get; set; }

    /// <summary>Dentist/doctor name.</summary>
    public string? DrName { get; set; }

    /// <summary>Material type.</summary>
    public string? Material { get; set; }

    /// <summary>Tooth number and description.</summary>
    public string? ToothInfo { get; set; }

    /// <summary>Prescription notes.</summary>
    public string? RxNotes { get; set; }

    /// <summary>Shipping method.</summary>
    public string? ShippingMethod { get; set; }

    /// <summary>When this data was cached.</summary>
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True if cache is older than 24 hours.</summary>
    public bool IsStale { get; set; }
}
