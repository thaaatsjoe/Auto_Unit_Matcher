using Microsoft.Data.Sqlite;

// Database path
var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AUM", "fingerprints.db");

Console.WriteLine($"Database: {dbPath}");

// Ensure directory exists
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Create users table if not exists
using var createTable = connection.CreateCommand();
createTable.CommandText = @"
    CREATE TABLE IF NOT EXISTS users (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        employee_number TEXT NOT NULL UNIQUE,
        employee_name TEXT NOT NULL,
        is_active BOOLEAN DEFAULT TRUE,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )";
createTable.ExecuteNonQuery();

// Insert admin user
using var insert = connection.CreateCommand();
insert.CommandText = @"
    INSERT OR REPLACE INTO users (employee_number, employee_name, is_active)
    VALUES (@num, @name, 1)";
insert.Parameters.AddWithValue("@num", "123456");
insert.Parameters.AddWithValue("@name", "Admin");
insert.ExecuteNonQuery();

Console.WriteLine("Created user: Admin (employee #123456)");
Console.WriteLine("Use employee number '123456' to log in.");
