using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for user/operator management.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>Gets a user by employee number.</summary>
    Task<User?> GetByEmployeeNumberAsync(string employeeNumber);
    
    /// <summary>Gets all active users.</summary>
    Task<IReadOnlyList<User>> GetAllActiveAsync();
    
    /// <summary>Deactivates a user (sets IsActive = false).</summary>
    Task<bool> DeactivateAsync(string employeeNumber);
    
    /// <summary>Checks if an employee number exists.</summary>
    Task<bool> ExistsAsync(string employeeNumber);
}
