using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the ABS cache repository.
/// </summary>
public class AbsCacheRepository : IAbsCacheRepository
{
    private readonly DatabaseContext _context;

    public AbsCacheRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<AbsCacheEntry?> GetByCaseIdAsync(string caseId)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM abs_cache WHERE case_id = @caseId;";
        command.Parameters.AddWithValue("@caseId", caseId);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToAbsCacheEntry(reader);
        }
        return null;
    }

    public async Task UpsertAsync(AbsCacheEntry entry)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO abs_cache (case_id, due_date, product_name, patient_name, dr_name,
                                   material, tooth_info, rx_notes, shipping_method, cached_at, is_stale)
            VALUES (@caseId, @dueDate, @productName, @patientName, @drName,
                    @material, @toothInfo, @rxNotes, @shippingMethod, @cachedAt, @isStale)
            ON CONFLICT(case_id) DO UPDATE SET
                due_date = excluded.due_date,
                product_name = excluded.product_name,
                patient_name = excluded.patient_name,
                dr_name = excluded.dr_name,
                material = excluded.material,
                tooth_info = excluded.tooth_info,
                rx_notes = excluded.rx_notes,
                shipping_method = excluded.shipping_method,
                cached_at = excluded.cached_at,
                is_stale = excluded.is_stale;";
        
        command.Parameters.AddWithValue("@caseId", entry.CaseId);
        command.Parameters.AddWithValue("@dueDate", (object?)entry.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@productName", (object?)entry.ProductName ?? DBNull.Value);
        command.Parameters.AddWithValue("@patientName", (object?)entry.PatientName ?? DBNull.Value);
        command.Parameters.AddWithValue("@drName", (object?)entry.DrName ?? DBNull.Value);
        command.Parameters.AddWithValue("@material", (object?)entry.Material ?? DBNull.Value);
        command.Parameters.AddWithValue("@toothInfo", (object?)entry.ToothInfo ?? DBNull.Value);
        command.Parameters.AddWithValue("@rxNotes", (object?)entry.RxNotes ?? DBNull.Value);
        command.Parameters.AddWithValue("@shippingMethod", (object?)entry.ShippingMethod ?? DBNull.Value);
        command.Parameters.AddWithValue("@cachedAt", entry.CachedAt.ToString("o"));
        command.Parameters.AddWithValue("@isStale", entry.IsStale ? 1 : 0);
        
        await command.ExecuteNonQueryAsync();
    }

    public async Task<int> MarkStaleAsync(TimeSpan maxAge)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        var cutoff = DateTime.UtcNow - maxAge;
        command.CommandText = @"
            UPDATE abs_cache SET is_stale = 1 
            WHERE cached_at < @cutoff AND is_stale = 0;";
        
        command.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
        
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<int> DeleteStaleAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "DELETE FROM abs_cache WHERE is_stale = 1;";
        
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<AbsCacheEntry>> GetAllFreshAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM abs_cache WHERE is_stale = 0;";
        
        var results = new List<AbsCacheEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToAbsCacheEntry(reader));
        }
        return results;
    }

    private static AbsCacheEntry MapToAbsCacheEntry(SqliteDataReader reader)
    {
        return new AbsCacheEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            CaseId = reader.GetString(reader.GetOrdinal("case_id")),
            DueDate = reader.IsDBNull(reader.GetOrdinal("due_date")) ? null : reader.GetString(reader.GetOrdinal("due_date")),
            ProductName = reader.IsDBNull(reader.GetOrdinal("product_name")) ? null : reader.GetString(reader.GetOrdinal("product_name")),
            PatientName = reader.IsDBNull(reader.GetOrdinal("patient_name")) ? null : reader.GetString(reader.GetOrdinal("patient_name")),
            DrName = reader.IsDBNull(reader.GetOrdinal("dr_name")) ? null : reader.GetString(reader.GetOrdinal("dr_name")),
            Material = reader.IsDBNull(reader.GetOrdinal("material")) ? null : reader.GetString(reader.GetOrdinal("material")),
            ToothInfo = reader.IsDBNull(reader.GetOrdinal("tooth_info")) ? null : reader.GetString(reader.GetOrdinal("tooth_info")),
            RxNotes = reader.IsDBNull(reader.GetOrdinal("rx_notes")) ? null : reader.GetString(reader.GetOrdinal("rx_notes")),
            ShippingMethod = reader.IsDBNull(reader.GetOrdinal("shipping_method")) ? null : reader.GetString(reader.GetOrdinal("shipping_method")),
            CachedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("cached_at"))),
            IsStale = reader.GetInt32(reader.GetOrdinal("is_stale")) == 1
        };
    }
}
