#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.Sqlite, 8.0.0"

using Microsoft.Data.Sqlite;
using System;
using System.IO;

var dbPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AUM", "aum.db");

Console.WriteLine($"Checking database: {dbPath}");
Console.WriteLine($"Database exists: {File.Exists(dbPath)}");

if (File.Exists(dbPath))
{
    var fileInfo = new FileInfo(dbPath);
    Console.WriteLine($"Database size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine();
    
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.Open();
    
    // Check total units
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM units";
        var totalUnits = (long)cmd.ExecuteScalar();
        Console.WriteLine($"Total units in database: {totalUnits}");
    }
    
    // Check units with non-null descriptors
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM units WHERE descriptor_blob IS NOT NULL";
        var withDescriptors = (long)cmd.ExecuteScalar();
        Console.WriteLine($"Units with non-null descriptors: {withDescriptors}");
    }
    
    // Check units with non-empty descriptors
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM units WHERE descriptor_blob IS NOT NULL AND LENGTH(descriptor_blob) > 0";
        var withData = (long)cmd.ExecuteScalar();
        Console.WriteLine($"Units with actual descriptor data: {withData}");
    }
    
    Console.WriteLine();
    Console.WriteLine("Sample of first 5 units:");
    Console.WriteLine("ID | Case ID | Descriptor Size | Has Data");
    Console.WriteLine("---|---------|-----------------|----------");
    
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
            
            Console.WriteLine($"{id} | {caseId} | {size} bytes | {hasData}");
        }
    }
    
    // Check average descriptor size
    using (var cmd = connection.CreateCommand())
    {
        cmd.CommandText = "SELECT AVG(LENGTH(descriptor_blob)) FROM units WHERE descriptor_blob IS NOT NULL";
        var avgSize = cmd.ExecuteScalar();
        if (avgSize != DBNull.Value)
        {
            Console.WriteLine();
            Console.WriteLine($"Average descriptor size: {Convert.ToDouble(avgSize):F2} bytes");
        }
    }
}
