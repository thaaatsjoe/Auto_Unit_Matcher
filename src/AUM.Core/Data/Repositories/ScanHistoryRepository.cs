using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the scan history repository.
/// </summary>
public class ScanHistoryRepository : IScanHistoryRepository
{
    private readonly DatabaseContext _context;

    public ScanHistoryRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> AddAsync(ScanHistoryEntry entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO scan_history (scanned_at, employee_number, employee_name, station_id, 
                                      scanner_id, matched_unit_id, confidence, confirmed,
                                      is_duplicate, is_reprint, operator_notes)
            VALUES (@scannedAt, @empNumber, @empName, @stationId, 
                    @scannerId, @matchedUnitId, @confidence, @confirmed,
                    @isDuplicate, @isReprint, @notes);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@scannedAt", entity.ScannedAt.ToString("o"));
        command.Parameters.AddWithValue("@empNumber", entity.EmployeeNumber);
        command.Parameters.AddWithValue("@empName", entity.EmployeeName);
        command.Parameters.AddWithValue("@stationId", entity.StationId);
        command.Parameters.AddWithValue("@scannerId", entity.ScannerId);
        command.Parameters.AddWithValue("@matchedUnitId", (object?)entity.MatchedUnitId ?? DBNull.Value);
        command.Parameters.AddWithValue("@confidence", (object?)entity.Confidence ?? DBNull.Value);
        command.Parameters.AddWithValue("@confirmed", entity.Confirmed.HasValue ? (entity.Confirmed.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("@isDuplicate", entity.IsDuplicate ? 1 : 0);
        command.Parameters.AddWithValue("@isReprint", entity.IsReprint ? 1 : 0);
        command.Parameters.AddWithValue("@notes", (object?)entity.OperatorNotes ?? DBNull.Value);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<ScanHistoryEntry?> GetByIdAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM scan_history WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToScanHistoryEntry(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetRecentByUnitAsync(long unitId, TimeSpan window)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        var cutoff = DateTime.UtcNow - window;
        command.CommandText = @"
            SELECT * FROM scan_history 
            WHERE matched_unit_id = @unitId AND scanned_at >= @cutoff AND confirmed = 1
            ORDER BY scanned_at DESC;";
        
        command.Parameters.AddWithValue("@unitId", unitId);
        command.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
        
        var results = new List<ScanHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToScanHistoryEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetByDateRangeAsync(DateTime start, DateTime end)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT * FROM scan_history 
            WHERE scanned_at >= @start AND scanned_at <= @end
            ORDER BY scanned_at DESC;";
        
        command.Parameters.AddWithValue("@start", start.ToString("o"));
        command.Parameters.AddWithValue("@end", end.ToString("o"));
        
        var results = new List<ScanHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToScanHistoryEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetByEmployeeAsync(string employeeNumber, int limit = 100)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT * FROM scan_history 
            WHERE employee_number = @empNumber
            ORDER BY scanned_at DESC
            LIMIT @limit;";
        
        command.Parameters.AddWithValue("@empNumber", employeeNumber);
        command.Parameters.AddWithValue("@limit", limit);
        
        var results = new List<ScanHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToScanHistoryEntry(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<ScanHistoryEntry>> GetAllAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM scan_history ORDER BY scanned_at DESC;";
        
        var results = new List<ScanHistoryEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToScanHistoryEntry(reader));
        }
        return results;
    }

    public async Task<ScanStatistics> GetStatsAsync(DateTime start, DateTime end)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT 
                COUNT(*) as total,
                SUM(CASE WHEN confirmed = 1 THEN 1 ELSE 0 END) as confirmed,
                SUM(CASE WHEN matched_unit_id IS NULL THEN 1 ELSE 0 END) as no_match,
                SUM(CASE WHEN is_duplicate = 1 THEN 1 ELSE 0 END) as duplicates,
                SUM(CASE WHEN is_reprint = 1 THEN 1 ELSE 0 END) as reprints,
                AVG(confidence) as avg_confidence
            FROM scan_history 
            WHERE scanned_at >= @start AND scanned_at <= @end;";
        
        command.Parameters.AddWithValue("@start", start.ToString("o"));
        command.Parameters.AddWithValue("@end", end.ToString("o"));
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ScanStatistics
            {
                TotalScans = reader.GetInt32(0),
                ConfirmedMatches = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                NoMatches = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Duplicates = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                Reprints = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                AverageConfidence = reader.IsDBNull(5) ? 0 : reader.GetDouble(5)
            };
        }
        return new ScanStatistics();
    }

    public async Task<bool> UpdateAsync(ScanHistoryEntry entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            UPDATE scan_history 
            SET confirmed = @confirmed, is_duplicate = @isDuplicate, 
                is_reprint = @isReprint, operator_notes = @notes
            WHERE id = @id;";
        
        command.Parameters.AddWithValue("@id", entity.Id);
        command.Parameters.AddWithValue("@confirmed", entity.Confirmed.HasValue ? (entity.Confirmed.Value ? 1 : 0) : DBNull.Value);
        command.Parameters.AddWithValue("@isDuplicate", entity.IsDuplicate ? 1 : 0);
        command.Parameters.AddWithValue("@isReprint", entity.IsReprint ? 1 : 0);
        command.Parameters.AddWithValue("@notes", (object?)entity.OperatorNotes ?? DBNull.Value);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "DELETE FROM scan_history WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<int> CountAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT COUNT(*) FROM scan_history;";
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static ScanHistoryEntry MapToScanHistoryEntry(SqliteDataReader reader)
    {
        return new ScanHistoryEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            ScannedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("scanned_at"))),
            EmployeeNumber = reader.GetString(reader.GetOrdinal("employee_number")),
            EmployeeName = reader.GetString(reader.GetOrdinal("employee_name")),
            StationId = reader.GetString(reader.GetOrdinal("station_id")),
            ScannerId = reader.GetString(reader.GetOrdinal("scanner_id")),
            MatchedUnitId = reader.IsDBNull(reader.GetOrdinal("matched_unit_id")) ? null : reader.GetInt64(reader.GetOrdinal("matched_unit_id")),
            Confidence = reader.IsDBNull(reader.GetOrdinal("confidence")) ? null : (float)reader.GetDouble(reader.GetOrdinal("confidence")),
            Confirmed = reader.IsDBNull(reader.GetOrdinal("confirmed")) ? null : reader.GetInt32(reader.GetOrdinal("confirmed")) == 1,
            IsDuplicate = reader.GetInt32(reader.GetOrdinal("is_duplicate")) == 1,
            IsReprint = reader.GetInt32(reader.GetOrdinal("is_reprint")) == 1,
            OperatorNotes = reader.IsDBNull(reader.GetOrdinal("operator_notes")) ? null : reader.GetString(reader.GetOrdinal("operator_notes"))
        };
    }
}
