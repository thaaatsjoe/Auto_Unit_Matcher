namespace AUM.Core.Interop;

/// <summary>
/// Error codes returned by the native AUM engine.
/// Maps to AUM_ErrorCode in exports.h.
/// </summary>
public enum ErrorCode
{
    /// <summary>Operation completed successfully.</summary>
    Success = 0,
    
    /// <summary>A null pointer was passed to the function.</summary>
    NullPointer = -1,
    
    /// <summary>The specified file was not found.</summary>
    FileNotFound = -2,
    
    /// <summary>The file format is invalid or corrupted.</summary>
    InvalidFormat = -3,
    
    /// <summary>A computation failed (e.g., descriptor extraction).</summary>
    ComputationFailed = -4,
    
    /// <summary>Memory allocation failed.</summary>
    OutOfMemory = -5,
    
    /// <summary>The index is not ready for querying (needs training).</summary>
    IndexNotReady = -6,
    
    /// <summary>An invalid handle was passed to the function.</summary>
    InvalidHandle = -7
}
