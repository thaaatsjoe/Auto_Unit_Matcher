using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for scan history records.
/// </summary>
public interface IScanHistoryRepository : IRepository<ScanHistoryEntry>
{
    /// <summary>Gets recent scans for a unit ID (for duplicate detection).</summary>
    Task<IReadOnlyList<ScanHistoryEntry>> GetRecentByUnitAsync(long unitId, TimeSpan window);
    
    /// <summary>Gets scans by date range.</summary>
    Task<IReadOnlyList<ScanHistoryEntry>> GetByDateRangeAsync(DateTime start, DateTime end);
    
    /// <summary>Gets scans by employee.</summary>
    Task<IReadOnlyList<ScanHistoryEntry>> GetByEmployeeAsync(string employeeNumber, int limit = 100);
    
    /// <summary>Gets statistics for a date range.</summary>
    Task<ScanStatistics> GetStatsAsync(DateTime start, DateTime end);
}

/// <summary>
/// Scan statistics summary.
/// </summary>
public class ScanStatistics
{
    public int TotalScans { get; set; }
    public int ConfirmedMatches { get; set; }
    public int NoMatches { get; set; }
    public int Duplicates { get; set; }
    public int Reprints { get; set; }
    public double AverageConfidence { get; set; }
}
