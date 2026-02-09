using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the remilling flag repository.
/// </summary>
public class RemillingFlagRepository : IRemillingFlagRepository
{
    private readonly DatabaseContext _context;

    public RemillingFlagRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> AddAsync(RemillingFlag flag)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO remilling_flags (case_id, flagged_at, flagged_by, notes)
            VALUES (@caseId, @flaggedAt, @flaggedBy, @notes);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@caseId", flag.CaseId);
        command.Parameters.AddWithValue("@flaggedAt", flag.FlaggedAt.ToString("o"));
        command.Parameters.AddWithValue("@flaggedBy", flag.FlaggedBy);
        command.Parameters.AddWithValue("@notes", (object?)flag.Notes ?? DBNull.Value);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<RemillingFlag?> GetByCaseIdAsync(string caseId)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM remilling_flags WHERE case_id = @caseId;";
        command.Parameters.AddWithValue("@caseId", caseId);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToRemillingFlag(reader);
        }
        return null;
    }

    public async Task<bool> IsFlaggedAsync(string caseId)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT 1 FROM remilling_flags WHERE case_id = @caseId LIMIT 1;";
        command.Parameters.AddWithValue("@caseId", caseId);
        
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<bool> RemoveAsync(string caseId)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "DELETE FROM remilling_flags WHERE case_id = @caseId;";
        command.Parameters.AddWithValue("@caseId", caseId);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<IReadOnlyList<RemillingFlag>> GetAllAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM remilling_flags ORDER BY flagged_at DESC;";
        
        var results = new List<RemillingFlag>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToRemillingFlag(reader));
        }
        return results;
    }

    private static RemillingFlag MapToRemillingFlag(SqliteDataReader reader)
    {
        return new RemillingFlag
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            CaseId = reader.GetString(reader.GetOrdinal("case_id")),
            FlaggedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("flagged_at"))),
            FlaggedBy = reader.GetString(reader.GetOrdinal("flagged_by")),
            Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes"))
        };
    }
}
