using AUM.Core.Models;
using Microsoft.Data.Sqlite;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// SQLite implementation of the user repository.
/// </summary>
public class UserRepository : IUserRepository
{
    private readonly DatabaseContext _context;

    public UserRepository(DatabaseContext context)
    {
        _context = context;
    }

    public async Task<long> AddAsync(User entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            INSERT INTO users (employee_number, employee_name, is_active, created_at)
            VALUES (@empNumber, @empName, @isActive, @createdAt);
            SELECT last_insert_rowid();";
        
        command.Parameters.AddWithValue("@empNumber", entity.EmployeeNumber);
        command.Parameters.AddWithValue("@empName", entity.EmployeeName);
        command.Parameters.AddWithValue("@isActive", entity.IsActive ? 1 : 0);
        command.Parameters.AddWithValue("@createdAt", entity.CreatedAt.ToString("o"));
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    public async Task<User?> GetByIdAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM users WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUser(reader);
        }
        return null;
    }

    public async Task<User?> GetByEmployeeNumberAsync(string employeeNumber)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM users WHERE employee_number = @empNumber;";
        command.Parameters.AddWithValue("@empNumber", employeeNumber);
        
        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToUser(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM users ORDER BY employee_name;";
        
        var results = new List<User>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToUser(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<User>> GetAllActiveAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT * FROM users WHERE is_active = 1 ORDER BY employee_name;";
        
        var results = new List<User>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(MapToUser(reader));
        }
        return results;
    }

    public async Task<bool> DeactivateAsync(string employeeNumber)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "UPDATE users SET is_active = 0 WHERE employee_number = @empNumber;";
        command.Parameters.AddWithValue("@empNumber", employeeNumber);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> ExistsAsync(string employeeNumber)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT 1 FROM users WHERE employee_number = @empNumber LIMIT 1;";
        command.Parameters.AddWithValue("@empNumber", employeeNumber);
        
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<bool> UpdateAsync(User entity)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = @"
            UPDATE users 
            SET employee_number = @empNumber, employee_name = @empName, is_active = @isActive
            WHERE id = @id;";
        
        command.Parameters.AddWithValue("@id", entity.Id);
        command.Parameters.AddWithValue("@empNumber", entity.EmployeeNumber);
        command.Parameters.AddWithValue("@empName", entity.EmployeeName);
        command.Parameters.AddWithValue("@isActive", entity.IsActive ? 1 : 0);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "DELETE FROM users WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        
        var rows = await command.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<int> CountAsync()
    {
        var connection = _context.CreateConnection();
        using var command = connection.CreateCommand();
        
        command.CommandText = "SELECT COUNT(*) FROM users;";
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static User MapToUser(SqliteDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            EmployeeNumber = reader.GetString(reader.GetOrdinal("employee_number")),
            EmployeeName = reader.GetString(reader.GetOrdinal("employee_name")),
            IsActive = reader.GetInt32(reader.GetOrdinal("is_active")) == 1,
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at")))
        };
    }
}
