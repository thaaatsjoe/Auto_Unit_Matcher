using AUM.Core.Data;
using Xunit;

namespace AUM.Tests.Data;

/// <summary>
/// Tests for DatabaseContext - schema creation and integrity checking.
/// </summary>
[Trait("Category", "Database")]
public class DatabaseContextTests : IDisposable
{
    private readonly DatabaseContext _context;

    public DatabaseContextTests()
    {
        _context = DatabaseContext.CreateInMemory();
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_CreatesAllTables()
    {
        // Act
        await _context.InitializeAsync();

        // Assert - verify tables exist by querying them
        await using var connection = _context.CreateConnection();
        await using var command = connection.CreateCommand();
        
        var tables = new[] { "units", "users", "audit_log", "scan_history", "abs_cache", "extraction_queue", "remilling_flags", "schema_version" };
        
        foreach (var table in tables)
        {
            command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{table}';";
            var result = await command.ExecuteScalarAsync();
            Assert.NotNull(result);
            Assert.Equal(table, result?.ToString());
        }
    }

    [Fact]
    public async Task CheckIntegrityAsync_ReturnsTrue_AfterInitialization()
    {
        // Arrange
        await _context.InitializeAsync();

        // Act
        var result = await _context.CheckIntegrityAsync();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetSchemaVersionAsync_Returns1_AfterInitialization()
    {
        // Arrange
        await _context.InitializeAsync();

        // Act
        var version = await _context.GetSchemaVersionAsync();

        // Assert
        Assert.Equal(1, version);
    }

    [Fact]
    public async Task CreateConnection_ReturnsOpenConnection()
    {
        // Arrange
        await _context.InitializeAsync();

        // Act
        await using var connection = _context.CreateConnection();

        // Assert
        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task InitializeAsync_CreatesIndexes()
    {
        // Arrange & Act
        await _context.InitializeAsync();

        // Assert - verify indexes exist
        await using var connection = _context.CreateConnection();
        await using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name LIKE 'idx_%';";
        
        var indexes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }

        Assert.Contains("idx_units_case_id", indexes);
        Assert.Contains("idx_audit_timestamp", indexes);
        Assert.Contains("idx_scan_history_date", indexes);
        Assert.Contains("idx_extraction_status", indexes);
    }
}
