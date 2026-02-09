namespace AUM.Core.Data.Repositories;

/// <summary>
/// Base repository interface with common CRUD operations.
/// </summary>
/// <typeparam name="T">Entity type.</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>Gets an entity by its primary key.</summary>
    Task<T?> GetByIdAsync(long id);
    
    /// <summary>Gets all entities.</summary>
    Task<IReadOnlyList<T>> GetAllAsync();
    
    /// <summary>Adds a new entity and returns its ID.</summary>
    Task<long> AddAsync(T entity);
    
    /// <summary>Updates an existing entity.</summary>
    Task<bool> UpdateAsync(T entity);
    
    /// <summary>Deletes an entity by ID.</summary>
    Task<bool> DeleteAsync(long id);
    
    /// <summary>Gets the count of all entities.</summary>
    Task<int> CountAsync();
}
