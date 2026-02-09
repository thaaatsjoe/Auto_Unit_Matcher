using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for remilling flags.
/// </summary>
public interface IRemillingFlagRepository
{
    /// <summary>Adds a remilling flag for a case.</summary>
    Task<long> AddAsync(RemillingFlag flag);
    
    /// <summary>Gets the flag for a case ID, if any.</summary>
    Task<RemillingFlag?> GetByCaseIdAsync(string caseId);
    
    /// <summary>Checks if a case has an active remilling flag.</summary>
    Task<bool> IsFlaggedAsync(string caseId);
    
    /// <summary>Removes a remilling flag.</summary>
    Task<bool> RemoveAsync(string caseId);
    
    /// <summary>Gets all active remilling flags.</summary>
    Task<IReadOnlyList<RemillingFlag>> GetAllAsync();
}
