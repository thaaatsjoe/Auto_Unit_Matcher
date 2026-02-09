using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the extraction queue repository.
/// </summary>
public class ExtractionQueueRepository : IExtractionQueueRepository
{
    private readonly DatabaseContext _context;

    public ExtractionQueueRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> EnqueueAsync(ExtractionQueueEntry entry)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO extraction_queue (stl_path, file_size_bytes, file_created_at, status, error_message)
            VALUES (@stlPath, @fileSize, @fileCreated, @status, @errorMessage);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@stlPath", entry.StlPath);
        command.Parameters.AddWithValue("@fileSize", entry.FileSizeBytes);
        command.Parameters.AddWithValue("@fileCreated", entry.FileCreatedAt.ToString("o"));
        command.Parameters.AddWithValue("@status", entry.Status.ToString().ToUpper());
        command.Parameters.AddWithValue("@errorMessage", (object?)entry.ErrorMessage ?? DBNull.Value);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<ExtractionQueueEntry?> GetNextPendingAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        // Get oldest pending file by file creation time
        command.CommandText = @"
            SELECT * FROM extraction_queue 
            WHERE status = 'PENDING'
            ORDER BY file_created_at ASC
            LIMIT 1;";
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToExtractionQueueEntry(reader);
        }
        return null;
    }

    public async Task<bool> UpdateStatusAsync(long id, ExtractionStatus status, string? errorMessage = null)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        var now = DateTime.UtcNow;
        
        if (status == ExtractionStatus.InProgress)
        {
            command.CommandText = @"
                UPDATE extraction_queue 
                SET status = @status, started_at = @startedAt, error_message = NULL
                WHERE id = @id;";
            command.Parameters.AddWithValue("@startedAt", now.ToString("o"));
        }
        else if (status == ExtractionStatus.Complete)
        {
            command.CommandText = @"
                UPDATE extraction_queue 
                SET status = @status, completed_at = @completedAt, error_message = NULL
                WHERE id = @id;";
            command.Parameters.AddWithValue("@completedAt", now.ToString("o"));
        }
        else if (status == ExtractionStatus.Failed)
        {
            command.CommandText = @"
                UPDATE extraction_queue 
                SET status = @status, completed_at = @completedAt, error_message = @errorMessage
                WHERE id = @id;";
            command.Parameters.AddWithValue("@completedAt", now.ToString("o"));
            command.Parameters.AddWithValue("@errorMessage", (object?)errorMessage ?? DBNull.Value);
        }
        else
        {
            command.CommandText = @"
                UPDATE extraction_queue 
                SET status = @status
                WHERE id = @id;";
        }
        
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status.ToString().ToUpper());
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<ExtractionProgress> GetProgressAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            SELECT 
                SUM(CASE WHEN status = 'PENDING' THEN 1 ELSE 0 END) as pending,
                SUM(CASE WHEN status = 'IN_PROGRESS' THEN 1 ELSE 0 END) as in_progress,
                SUM(CASE WHEN status = 'COMPLETE' THEN 1 ELSE 0 END) as complete,
                SUM(CASE WHEN status = 'FAILED' THEN 1 ELSE 0 END) as failed
            FROM extraction_queue;";
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new ExtractionProgress
            {
                Pending = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                InProgress = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                Complete = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                Failed = reader.IsDBNull(3) ? 0 : reader.GetInt32(3)
            };
        }
        return new ExtractionProgress();
    }

    public async Task<IReadOnlyList<ExtractionQueueEntry>> GetFailedAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM extraction_queue WHERE status = 'FAILED' ORDER BY completed_at DESC;";
        
        var results = new List<ExtractionQueueEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToExtractionQueueEntry(reader));
        }
        return results;
    }

    public async Task<int> RetryFailedAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            UPDATE extraction_queue 
            SET status = 'PENDING', error_message = NULL, started_at = NULL, completed_at = NULL
            WHERE status = 'FAILED';";
        
        return await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> ExistsAsync(string stlPath)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT 1 FROM extraction_queue WHERE stl_path = @stlPath LIMIT 1;";
        command.Parameters.AddWithValue("@stlPath", stlPath);
        
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<IReadOnlyList<ExtractionQueueEntry>> GetByStatusAsync(ExtractionStatus status)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM extraction_queue WHERE status = @status ORDER BY file_created_at ASC;";
        command.Parameters.AddWithValue("@status", status.ToString().ToUpper());
        
        var results = new List<ExtractionQueueEntry>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToExtractionQueueEntry(reader));
        }
        return results;
    }

    private static ExtractionQueueEntry MapToExtractionQueueEntry(SqliteDataReader reader)
    {
        var statusStr = reader.GetString(reader.GetOrdinal("status"));
        var status = statusStr switch
        {
            "PENDING" => ExtractionStatus.Pending,
            "IN_PROGRESS" => ExtractionStatus.InProgress,
            "COMPLETE" => ExtractionStatus.Complete,
            "FAILED" => ExtractionStatus.Failed,
            _ => ExtractionStatus.Pending
        };

        return new ExtractionQueueEntry
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            StlPath = reader.GetString(reader.GetOrdinal("stl_path")),
            FileSizeBytes = reader.GetInt64(reader.GetOrdinal("file_size_bytes")),
            FileCreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("file_created_at"))),
            Status = status,
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) ? null : reader.GetString(reader.GetOrdinal("error_message")),
            StartedAt = reader.IsDBNull(reader.GetOrdinal("started_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("completed_at")))
        };
    }
}
