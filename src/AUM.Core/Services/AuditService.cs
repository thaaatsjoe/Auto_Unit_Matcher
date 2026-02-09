using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for HIPAA-compliant audit logging.
/// </summary>
public class AuditService : IAuditService
{
    private readonly IAuditRepository _auditRepository;
    private readonly ILogger<AuditService>? _logger;
    
    // These would be set by the application at login/startup
    private string _currentEmployeeNumber = "SYSTEM";
    private string _currentEmployeeName = "System";
    private string _currentStationId = "UNKNOWN";
    
    public AuditService(
        IAuditRepository auditRepository,
        ILogger<AuditService>? logger = null)
    {
        _auditRepository = auditRepository;
        _logger = logger;
    }
    
    /// <summary>
    /// Sets the current operator context for audit logging.
    /// </summary>
    public void SetContext(string employeeNumber, string employeeName, string stationId)
    {
        _currentEmployeeNumber = employeeNumber;
        _currentEmployeeName = employeeName;
        _currentStationId = stationId;
        _logger?.LogInformation("Audit context set: Employee={Employee}, Station={Station}", 
            employeeNumber, stationId);
    }
    
    /// <inheritdoc/>
    public async Task LogAsync(string actionType, string? caseId = null, string? details = null)
    {
        if (string.IsNullOrWhiteSpace(actionType))
            throw new ArgumentException("Action type cannot be null or empty", nameof(actionType));
        
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EmployeeNumber = _currentEmployeeNumber,
            EmployeeName = _currentEmployeeName,
            StationId = _currentStationId,
            ActionType = actionType,
            CaseId = caseId,
            Details = details
        };
        
        await _auditRepository.LogAsync(entry);
        
        _logger?.LogDebug("Audit log: {Action} by {Employee} on {Station}", 
            actionType, _currentEmployeeNumber, _currentStationId);
    }
    
    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLogEntry>> GetRecentAsync(int count = 100)
    {
        var end = DateTime.UtcNow;
        var start = end.AddDays(-30); // Last 30 days
        
        var entries = await _auditRepository.GetByDateRangeAsync(start, end);
        return entries.Take(count);
    }
    
    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLogEntry>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        return await _auditRepository.GetByDateRangeAsync(start, end);
    }
    
    /// <inheritdoc/>
    public async Task<IEnumerable<AuditLogEntry>> GetByEmployeeAsync(string employeeNumber)
    {
        return await _auditRepository.GetByEmployeeAsync(employeeNumber);
    }
}
