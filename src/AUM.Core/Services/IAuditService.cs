using AUM.Core.Models;

namespace AUM.Core.Services;

/// <summary>
/// Service for HIPAA-compliant audit logging.
/// </summary>
public interface IAuditService
{
    /// <summary>
    /// Logs an action to the audit trail.
    /// </summary>
    /// <param name="actionType">Type of action (e.g., "MATCH_CONFIRMED", "UNIT_REGISTERED").</param>
    /// <param name="caseId">Optional case ID related to the action.</param>
    /// <param name="details">Optional additional details.</param>
    Task LogAsync(string actionType, string? caseId = null, string? details = null);
    
    /// <summary>
    /// Gets recent audit log entries.
    /// </summary>
    /// <param name="count">Number of entries to retrieve.</param>
    Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100);
    
    /// <summary>
    /// Gets audit log entries within a date range.
    /// </summary>
    Task<IEnumerable<AuditLogEntry>> GetByDateRangeAsync(DateTime start, DateTime end);
    
    /// <summary>
    /// Gets audit log entries for a specific employee.
    /// </summary>
    Task<IEnumerable<AuditLogEntry>> GetByEmployeeAsync(string employeeNumber);
}
