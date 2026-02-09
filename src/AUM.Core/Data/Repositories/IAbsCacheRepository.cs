using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for ABS API cache.
/// </summary>
public interface IAbsCacheRepository
{
    /// <summary>Gets cached data for a case ID.</summary>
    Task<AbsCacheEntry?> GetByCaseIdAsync(string caseId);
    
    /// <summary>Inserts or updates cache entry.</summary>
    Task UpsertAsync(AbsCacheEntry entry);
    
    /// <summary>Marks entries older than specified age as stale.</summary>
    Task<int> MarkStaleAsync(TimeSpan maxAge);
    
    /// <summary>Deletes all stale entries.</summary>
    Task<int> DeleteStaleAsync();
    
    /// <summary>Gets all non-stale entries.</summary>
    Task<IReadOnlyList<AbsCacheEntry>> GetAllFreshAsync();
}
