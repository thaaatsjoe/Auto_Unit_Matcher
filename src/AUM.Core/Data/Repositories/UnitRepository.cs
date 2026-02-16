using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the unit repository.
/// </summary>
public class UnitRepository : IUnitRepository
{
    private readonly DatabaseContext _context;

    public UnitRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> AddAsync(Unit entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO units (case_id, stl_path, descriptor_blob, created_at)
            VALUES (@caseId, @stlPath, @blob, @createdAt);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@caseId", entity.CaseId);
        command.Parameters.AddWithValue("@stlPath", entity.StlPath);
        command.Parameters.AddWithValue("@blob", entity.DescriptorBlob);
        command.Parameters.AddWithValue("@createdAt", entity.CreatedAt.ToString("o"));
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<Unit?> GetByIdAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM units WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUnit(reader);
        }
        return null;
    }

    public async Task<Unit?> GetByStlPathAsync(string stlPath)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM units WHERE stl_path = @stlPath;";
        command.Parameters.AddWithValue("@stlPath", stlPath);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUnit(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<Unit>> GetByCaseIdAsync(string caseId)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM units WHERE case_id = @caseId;";
        command.Parameters.AddWithValue("@caseId", caseId);
        
        var results = new List<Unit>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToUnit(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<Unit>> GetAllAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM units;";
        
        var results = new List<Unit>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToUnit(reader));
        }
        return results;
    }

    public async Task<bool> ExistsAsync(string stlPath)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT 1 FROM units WHERE stl_path = @stlPath LIMIT 1;";
        command.Parameters.AddWithValue("@stlPath", stlPath);
        
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<IReadOnlyList<(long Id, byte[] DescriptorBlob)>> GetAllDescriptorsAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT id, descriptor_blob FROM units;";
        
        var results = new List<(long, byte[])>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetInt64(0), (byte[])reader["descriptor_blob"]));
        }
        return results;
    }

    public async Task<bool> UpdateAsync(Unit entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            UPDATE units 
            SET case_id = @caseId, stl_path = @stlPath, descriptor_blob = @blob
            WHERE id = @id;";
        
        command.Parameters.AddWithValue("@id", entity.Id);
        command.Parameters.AddWithValue("@caseId", entity.CaseId);
        command.Parameters.AddWithValue("@stlPath", entity.StlPath);
        command.Parameters.AddWithValue("@blob", entity.DescriptorBlob);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "DELETE FROM units WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<int> CountAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT COUNT(*) FROM units;";
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <inheritdoc/>
    public async Task<long> UpsertByStlPathAsync(Unit entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO units (case_id, stl_path, descriptor_blob, created_at)
            VALUES (@caseId, @stlPath, @blob, @createdAt)
            ON CONFLICT(stl_path) DO UPDATE SET
                case_id = @caseId,
                descriptor_blob = @blob,
                created_at = @createdAt;
            SELECT id FROM units WHERE stl_path = @stlPath;";
        
        command.Parameters.AddWithValue("@caseId", entity.CaseId);
        command.Parameters.AddWithValue("@stlPath", entity.StlPath);
        command.Parameters.AddWithValue("@blob", entity.DescriptorBlob);
        command.Parameters.AddWithValue("@createdAt", entity.CreatedAt.ToString("o"));
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private static Unit MapToUnit(SqliteDataReader reader)
    {
        return new Unit
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            CaseId = reader.GetString(reader.GetOrdinal("case_id")),
            StlPath = reader.GetString(reader.GetOrdinal("stl_path")),
            DescriptorBlob = (byte[])reader["descriptor_blob"],
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }
}
