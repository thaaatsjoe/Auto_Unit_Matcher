using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the audit repository.
/// </summary>
public class AuditRepository : IAuditRepository
{
    private readonly DatabaseContext _context;

    public AuditRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> LogAsync(AuditLogEntry entry)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO audit_log (timestamp, employee_number, employee_name, station_id, 
                                   scanner_id, action_type, case_id, details)
            VALUES (@timestamp, @empNumber, @empName, @stationId, 
                    @scannerId, @actionType, @caseId, @details);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@timestamp", entry.Timestamp.ToString("o"));
        command.Parameters.AddWithValue("@empNumber", entry.EmployeeNumber);
        command.Parameters.AddWithValue("@empName", entry.EmployeeName);
        command.Parameters.AddWithValue("@stationId", entry.StationId);
        command.Parameters.AddWithValue("@scannerId", (object?)entry.ScannerId ?? DBNull.Value);
        command.Parameters.AddWithValue("@actionType", entry.ActionType);
        command.Parameters.AddWithValue("@caseId", (object?)entry.CaseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@details", (object?)entry.Details ?? DBNull.Value);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT * FROM audit_log 
            WHERE timestamp >= @start AND timestamp <= @end
            ORDER BY timestamp DESC;";
        
        command.Parameters.AddWithValue("@start", start.ToString("o"));
        command.Parameters.AddWithValue("@end", end.ToString("o"));
        
        var results = new List<AuditLogEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByEmployeeAsync(string employeeNumber, int limit = 100)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT * FROM audit_log 
            WHERE employee_number = @empNumber
            ORDER BY timestamp DESC
            LIMIT @limit;";
        
        command.Parameters.AddWithValue("@empNumber", employeeNumber);
        command.Parameters.AddWithValue("@limit", limit);
        
        var results = new List<AuditLogEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<AuditLogEntry>> GetByActionTypeAsync(string actionType, int limit = 100)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT * FROM audit_log 
            WHERE action_type = @actionType
            ORDER BY timestamp DESC
            LIMIT @limit;";
        
        command.Parameters.AddWithValue("@actionType", actionType);
        command.Parameters.AddWithValue("@limit", limit);
        
        var results = new List<AuditLogEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToAuditLogEntry(reader));
        }
        return results;
    }

    public async Task<int> CountByDateRangeAsync(DateTime start, DateTime end)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT COUNT(*) FROM audit_log 
            WHERE timestamp >= @start AND timestamp <= @end;";
        
        command.Parameters.AddWithValue("@start", start.ToString("o"));
        command.Parameters.AddWithValue("@end", end.ToString("o"));
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static AuditLogEntry MapToAuditLogEntry(SqliteDataReader reader)
    {
        return new AuditLogEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("timestamp"))),
            EmployeeNumber = reader.GetString(reader.GetOrdinal("employee_number")),
            EmployeeName = reader.GetString(reader.GetOrdinal("employee_name")),
            StationId = reader.GetString(reader.GetOrdinal("station_id")),
            ScannerId = reader.IsDBNull(reader.GetOrdinal("scanner_id")) ? null : reader.GetString(reader.GetOrdinal("scanner_id")),
            ActionType = reader.GetString(reader.GetOrdinal("action_type")),
            CaseId = reader.IsDBNull(reader.GetOrdinal("case_id")) ? null : reader.GetString(reader.GetOrdinal("case_id")),
            Details = reader.IsDBNull(reader.GetOrdinal("details")) ? null : reader.GetString(reader.GetOrdinal("details"))
        };
    }
}
