using AUM.Core.Interop;

namespace AUM.Core.Engine;

/// <summary>
/// Interface for the fingerprint engine.
/// Provides high-level operations for STL fingerprinting and matching.
/// v4.0: ISS Keypoints + SHOT352 + FAISS Voting + RANSAC + Dense Point-to-Plane ICP
/// </summary>
public interface IFingerprintEngine : IDisposable
{
    /// <summary>
    /// Extracts ISS keypoint + SHOT352 descriptors + dense cloud from an STL file.
    /// </summary>
    /// <param name="stlPath">Path to the STL file.</param>
    /// <returns>Serialized descriptor blob (SH02 format) for database storage.</returns>
    byte[] ExtractDescriptor(string stlPath);
    
    /// <summary>
    /// Stage 1: Query the FAISS index using keypoint voting.
    /// Returns top-K units by weighted vote score.
    /// </summary>
    /// <param name="descriptor">Serialized descriptor blob from scan.</param>
    /// <param name="topK">Number of top-voted units to return.</param>
    /// <returns>Array of vote results sorted by vote score.</returns>
    VoteResult[] QueryVotes(byte[] descriptor, int topK = 10);
    
    /// <summary>
    /// Stage 2: Geometric verification using RANSAC + Dense Point-to-Plane ICP.
    /// Verifies spatial alignment between a query and candidate descriptor.
    /// </summary>
    /// <param name="queryDescriptor">Serialized descriptor blob from scan.</param>
    /// <param name="candidateDescriptor">Serialized descriptor blob from database.</param>
    /// <returns>Verification result with RANSAC inliers, ICP fitness, final score.</returns>
    VerificationResult Verify(byte[] queryDescriptor, byte[] candidateDescriptor);
    
    /// <summary>
    /// Adds a descriptor to the FAISS index with the given unit ID.
    /// </summary>
    void AddToIndex(long id, byte[] descriptor);
    
    /// <summary>
    /// Finalizes the FlatL2 index. Call after adding all entries, before querying.
    /// </summary>
    void TrainIndex();
    
    /// <summary>
    /// Clears the index and recreates it empty.
    /// </summary>
    void ClearIndex();
    
    /// <summary>
    /// Saves the index to a file.
    /// </summary>
    /// <param name="path">Path to save the index to.</param>
    void SaveIndex(string path);
    
    /// <summary>
    /// Loads an index from a file.
    /// </summary>
    /// <param name="path">Path to load the index from.</param>
    void LoadIndex(string path);
    
    /// <summary>
    /// Gets the current number of units in the index.
    /// </summary>
    int IndexCount { get; }
    
    /// <summary>
    /// Gets the native engine version string.
    /// </summary>
    string Version { get; }
}
