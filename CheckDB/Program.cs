using Microsoft.Data.Sqlite;
using System.Text;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AUM", "fingerprints.db");

var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "db_report.txt");
var sb = new StringBuilder();

void Log(string msg) { sb.AppendLine(msg); Console.WriteLine(msg); }

Log($"Checking database: {dbPath}");
Log($"Database exists: {File.Exists(dbPath)}");

if (File.Exists(dbPath))
{
    var fileInfo = new FileInfo(dbPath);
    Log($"Database size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB\n");
    
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    
    // Check total units
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM units";
        var totalUnits = (long)cmd.ExecuteScalar()!;
        Log($"Total units in database: {totalUnits}");
    }
    
    // Check units with actual descriptor data
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM units WHERE descriptor_blob IS NOT NULL AND LENGTH(descriptor_blob) > 0";
        var withData = (long)cmd.ExecuteScalar()!;
        Log($"Units with actual descriptor data: {withData}");
    }
    
    Log("\nSample of first 5 units:");
    Log("ID | Case ID | Descriptor Size | Has Data");
    Log("---|---------|-----------------|----------");
    
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = @"
            SELECT 
                id, 
                case_id, 
                LENGTH(descriptor_blob) as size,
                CASE WHEN descriptor_blob IS NULL THEN 'NULL' ELSE 'YES' END as has_data
            FROM units 
            LIMIT 5";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt64(0);
            var caseId = reader.GetString(1);
            var size = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var hasData = reader.GetString(3);
            
            Log($"{id} | {caseId} | {size} bytes | {hasData}");
        }
    }
    
    // Check average descriptor size
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT AVG(LENGTH(descriptor_blob)) FROM units WHERE descriptor_blob IS NOT NULL";
        var avgSize = cmd.ExecuteScalar();
        if (avgSize != DBNull.Value)
        {
            Log($"\nAverage descriptor size: {Convert.ToDouble(avgSize):F2} bytes");
        }
    }
}

File.WriteAllText("db_report.txt", sb.ToString());
File.WriteAllText(reportPath, sb.ToString());
