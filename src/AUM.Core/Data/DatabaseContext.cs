using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AUM.Core.Data;

/// <summary>
/// Manages SQLite database connections and schema initialization.
/// </summary>
public class DatabaseContext : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseContext>? _logger;
    private readonly bool _isInMemory;
    private SqliteConnection? _sharedConnection;
    private bool _disposed;

    /// <summary>
    /// Creates a new database context with the specified connection string.
    /// </summary>
    /// <param name="connectionString">SQLite connection string.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="isInMemory">Whether this is an in-memory database (requires shared connection).</param>
    private DatabaseContext(string connectionString, ILogger<DatabaseContext>? logger, bool isInMemory)
    {
        _connectionString = connectionString;
        _logger = logger;
        _isInMemory = isInMemory;
    }

    /// <summary>
    /// Creates a new database context with the specified connection string (file-based).
    /// </summary>
    public DatabaseContext(string connectionString, ILogger<DatabaseContext>? logger = null)
        : this(connectionString, logger, false)
    {
    }

    /// <summary>
    /// Creates a database context for an in-memory database (for testing).
    /// </summary>
    public static DatabaseContext CreateInMemory(ILogger<DatabaseContext>? logger = null)
    {
        // Each in-memory database gets a unique name to prevent test conflicts
        var uniqueId = Guid.NewGuid().ToString("N");
        return new DatabaseContext($"Data Source=InMemoryTest_{uniqueId};Mode=Memory;Cache=Shared", logger, true);
    }

    /// <summary>
    /// Creates a database context for a file-based database.
    /// </summary>
    /// <param name="filePath">Path to the SQLite database file.</param>
    /// <param name="logger">Optional logger.</param>
    public static DatabaseContext CreateFromFile(string filePath, ILogger<DatabaseContext>? logger = null)
    {
        return new DatabaseContext($"Data Source={filePath}", logger, false);
    }

    /// <summary>
    /// Gets a connection to the database.
    /// For in-memory databases, always returns the same shared connection.
    /// For file-based databases, creates a new connection each time.
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        if (_isInMemory)
        {
            // In-memory databases are connection-specific - must reuse same connection
            if (_sharedConnection == null)
            {
                _sharedConnection = new SqliteConnection(_connectionString);
                _sharedConnection.Open();
            }
            return _sharedConnection;
        }
        
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Initializes the database schema.
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("Initializing database schema...");
        
        var schemaPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data", "Migrations", "Schema.sql");

        string schemaSql;
        
        // Try to read from file, fall back to embedded resource
        if (File.Exists(schemaPath))
        {
            schemaSql = await File.ReadAllTextAsync(schemaPath);
        }
        else
        {
            // Use embedded schema as fallback
            schemaSql = GetEmbeddedSchema();
        }

        var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = schemaSql;
        await command.ExecuteNonQueryAsync();
        
        // Don't dispose connection for in-memory databases
        if (!_isInMemory)
        {
            await connection.DisposeAsync();
        }
        
        _logger?.LogInformation("Database schema initialized successfully.");
    }

    /// <summary>
    /// Runs SQLite integrity check.
    /// </summary>
    /// <returns>True if database passes integrity check.</returns>
    public async Task<bool> CheckIntegrityAsync()
    {
        var connection = CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        
        var result = await command.ExecuteScalarAsync();
        
        if (!_isInMemory)
        {
            await connection.DisposeAsync();
        }
        
        var isOk = result?.ToString() == "ok";
        
        if (!isOk)
        {
            _logger?.LogError("Database integrity check failed: {Result}", result);
        }
        
        return isOk;
    }

    /// <summary>
    /// Gets the current schema version.
    /// </summary>
    public async Task<int> GetSchemaVersionAsync()
    {
        try
        {
            var connection = CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MAX(version) FROM schema_version;";
            
            var result = await command.ExecuteScalarAsync();
            
            if (!_isInMemory)
            {
                await connection.DisposeAsync();
            }
            
            return result == DBNull.Value ? 0 : Convert.ToInt32(result);
        }
        catch
        {
            return 0; // Table doesn't exist yet
        }
    }

    private static string GetEmbeddedSchema()
    {
        // Embedded fallback schema (minimal version for testing)
        return @"
CREATE TABLE IF NOT EXISTS units (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,
    stl_path TEXT NOT NULL UNIQUE,
    descriptor_blob BLOB NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_units_case_id ON units(case_id);

CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_number TEXT NOT NULL UNIQUE,
    employee_name TEXT NOT NULL,
    is_active INTEGER DEFAULT 1,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS audit_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,
    scanner_id TEXT,
    action_type TEXT NOT NULL,
    case_id TEXT,
    details TEXT
);
CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON audit_log(timestamp);

CREATE TABLE IF NOT EXISTS scan_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    scanned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    employee_number TEXT NOT NULL,
    employee_name TEXT NOT NULL,
    station_id TEXT NOT NULL,
    scanner_id TEXT NOT NULL,
    matched_unit_id INTEGER,
    confidence REAL,
    confirmed INTEGER,
    is_duplicate INTEGER DEFAULT 0,
    is_reprint INTEGER DEFAULT 0,
    operator_notes TEXT
);
CREATE INDEX IF NOT EXISTS idx_scan_history_date ON scan_history(scanned_at);

CREATE TABLE IF NOT EXISTS abs_cache (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL UNIQUE,
    due_date TEXT,
    product_name TEXT,
    patient_name TEXT,
    dr_name TEXT,
    material TEXT,
    tooth_info TEXT,
    rx_notes TEXT,
    shipping_method TEXT,
    cached_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    is_stale INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS extraction_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    stl_path TEXT NOT NULL UNIQUE,
    file_size_bytes INTEGER NOT NULL,
    file_created_at TIMESTAMP NOT NULL,
    status TEXT NOT NULL DEFAULT 'PENDING',
    error_message TEXT,
    started_at TIMESTAMP,
    completed_at TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_extraction_status ON extraction_queue(status);

CREATE TABLE IF NOT EXISTS remilling_flags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL UNIQUE,
    flagged_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    flagged_by TEXT NOT NULL,
    notes TEXT
);

CREATE TABLE IF NOT EXISTS schema_version (
    version INTEGER PRIMARY KEY,
    applied_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
INSERT OR IGNORE INTO schema_version (version) VALUES (1);
";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _sharedConnection?.Dispose();
        _disposed = true;
    }
}
