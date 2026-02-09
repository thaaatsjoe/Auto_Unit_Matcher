using AUM.Core.Interop;

namespace AUM.Core.Engine;

/// <summary>
/// Exception thrown when the native engine encounters an error.
/// </summary>
public class EngineException : Exception
{
    /// <summary>
    /// The error code returned by the native engine.
    /// </summary>
    public ErrorCode ErrorCode { get; }
    
    /// <summary>
    /// Creates a new EngineException.
    /// </summary>
    public EngineException(ErrorCode errorCode, string? message = null)
        : base(message ?? GetDefaultMessage(errorCode))
    {
        ErrorCode = errorCode;
    }
    
    /// <summary>
    /// Creates a new EngineException with an inner exception.
    /// </summary>
    public EngineException(ErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
    
    private static string GetDefaultMessage(ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.Success => "Operation completed successfully",
            ErrorCode.NullPointer => "A null pointer was passed to the engine",
            ErrorCode.FileNotFound => "The specified file was not found",
            ErrorCode.InvalidFormat => "The file format is invalid or corrupted",
            ErrorCode.ComputationFailed => "A computation failed in the engine",
            ErrorCode.OutOfMemory => "Memory allocation failed",
            ErrorCode.IndexNotReady => "The index is not ready for querying (needs training)",
            ErrorCode.InvalidHandle => "An invalid handle was passed to the engine",
            _ => $"Unknown engine error: {errorCode}"
        };
    }
    
    /// <summary>
    /// Throws an EngineException if the error code is not Success.
    /// </summary>
    public static void ThrowIfError(ErrorCode errorCode)
    {
        if (errorCode != ErrorCode.Success)
        {
            string message;
            try
            {
                var nativeMessage = NativeMethods.GetLastErrorMessage();
                message = string.IsNullOrEmpty(nativeMessage)
                    ? GetDefaultMessage(errorCode)
                    : nativeMessage;
            }
            catch (DllNotFoundException)
            {
                // DLL not available, use default message
                message = GetDefaultMessage(errorCode);
            }
            throw new EngineException(errorCode, message);
        }
    }
}
