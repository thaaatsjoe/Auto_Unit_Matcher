using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for audit log entries.
/// </summary>
public interface IAuditRepository
{
    /// <summary>Logs an action.</summary>
    Task<long> LogAsync(AuditLogEntry entry);
    
    /// <summary>Gets entries by date range.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetByDateRangeAsync(DateTime start, DateTime end);
    
    /// <summary>Gets entries by employee number.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetByEmployeeAsync(string employeeNumber, int limit = 100);
    
    /// <summary>Gets entries by action type.</summary>
    Task<IReadOnlyList<AuditLogEntry>> GetByActionTypeAsync(string actionType, int limit = 100);
    
    /// <summary>Gets count of entries in date range.</summary>
    Task<int> CountByDateRangeAsync(DateTime start, DateTime end);
}
