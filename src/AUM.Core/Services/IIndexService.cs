namespace AUM.Core.Services;

/// <summary>
/// Service for managing the FAISS search index.
/// </summary>
public interface IIndexService
{
    /// <summary>
    /// Initializes the index, loading from disk if available.
    /// </summary>
    Task InitializeAsync();
    
    /// <summary>
    /// Adds a descriptor to the index.
    /// </summary>
    /// <param name="unitId">Database ID of the unit.</param>
    /// <param name="descriptor">Serialized descriptor blob.</param>
    Task AddAsync(long unitId, byte[] descriptor);
    
    /// <summary>
    /// Rebuilds the index from all units in the database.
    /// </summary>
    Task RebuildAsync();
    
    /// <summary>
    /// Trains the index (required before querying).
    /// </summary>
    Task TrainAsync();
    
    /// <summary>
    /// Saves the index to disk.
    /// </summary>
    Task SaveAsync();
    
    /// <summary>
    /// Gets the number of entries in the index.
    /// </summary>
    int Count { get; }
    
    /// <summary>
    /// Gets whether the index is trained and ready for queries.
    /// </summary>
    bool IsReady { get; }
}
