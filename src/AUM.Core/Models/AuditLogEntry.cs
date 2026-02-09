namespace AUM.Core.Models;

/// <summary>
/// Audit trail entry for HIPAA compliance.
/// Every action is logged with operator, timestamp, and context.
/// </summary>
public class AuditLogEntry
{
    public long Id { get; set; }

    /// <summary>When the action occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Employee number of the operator who performed the action.</summary>
    public required string EmployeeNumber { get; set; }

    /// <summary>Name of operator (denormalized for reports).</summary>
    public required string EmployeeName { get; set; }

    /// <summary>Which workstation (for multi-station setup).</summary>
    public required string StationId { get; set; }

    /// <summary>Which scanner (1 or 2), null for non-scan actions.</summary>
    public string? ScannerId { get; set; }

    /// <summary>Action type: SCAN, CONFIRM, REJECT, REPRINT, LOGIN, LOGOUT, etc.</summary>
    public required string ActionType { get; set; }

    /// <summary>Associated case ID, null for non-case actions.</summary>
    public string? CaseId { get; set; }

    /// <summary>Additional context or details.</summary>
    public string? Details { get; set; }
}
