using AUM.Core.Interop;

namespace AUM.Core.Engine;

/// <summary>
/// Interface for the fingerprint engine.
/// Provides high-level operations for STL fingerprinting and matching.
/// </summary>
public interface IFingerprintEngine : IDisposable
{
    /// <summary>
    /// Extracts a fingerprint descriptor from an STL file.
    /// </summary>
    /// <param name="stlPath">Path to the STL file.</param>
    /// <returns>Serialized descriptor blob for database storage.</returns>
    byte[] ExtractDescriptor(string stlPath);
    
    /// <summary>
    /// Queries the index for matches to the given descriptor.
    /// </summary>
    /// <param name="descriptor">Serialized descriptor blob.</param>
    /// <param name="topK">Number of results to return (default 5 per PRD).</param>
    /// <returns>Array of match results, ordered by similarity.</returns>
    MatchResult[] Query(byte[] descriptor, int topK = 5);
    
    /// <summary>
    /// Adds a descriptor to the index with the given ID.
    /// </summary>
    /// <param name="id">Database ID of the unit.</param>
    /// <param name="descriptor">Serialized descriptor blob.</param>
    void AddToIndex(long id, byte[] descriptor);
    
    /// <summary>
    /// Trains the index. Call after adding entries, before querying.
    /// </summary>
    void TrainIndex();
    
    /// <summary>
    /// Saves the index to a file.
    /// </summary>
    /// <param name="path">Path to save the index.</param>
    void SaveIndex(string path);
    
    /// <summary>
    /// Loads an index from a file.
    /// </summary>
    /// <param name="path">Path to load the index from.</param>
    void LoadIndex(string path);
    
    /// <summary>
    /// Compares two descriptors point-to-point for partial matching.
    /// Returns a score (0-100%) based on how many query points match candidate points.
    /// </summary>
    /// <param name="queryDescriptor">Serialized descriptor blob from partial scan.</param>
    /// <param name="candidateDescriptor">Serialized descriptor blob from database.</param>
    /// <returns>Match score 0-100%.</returns>
    float CompareDescriptors(byte[] queryDescriptor, byte[] candidateDescriptor);
    
    /// <summary>
    /// Gets the current number of entries in the index.
    /// </summary>
    int IndexCount { get; }
    
    /// <summary>
    /// Gets the native engine version string.
    /// </summary>
    string Version { get; }
}
