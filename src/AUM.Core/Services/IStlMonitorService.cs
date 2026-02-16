namespace AUM.Core.Services;

/// <summary>
/// Event args for STL file detection.
/// </summary>
public class StlFileEventArgs : EventArgs
{
    /// <summary>Full path to the STL file.</summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>Case ID parsed from folder structure.</summary>
    public string CaseId { get; set; } = string.Empty;
}

/// <summary>
/// Service for monitoring directories for new STL files.
/// </summary>
public interface IStlMonitorService : IDisposable
{
    /// <summary>
    /// Starts monitoring a directory for new STL files.
    /// </summary>
    /// <param name="rootPath">Root directory to monitor.</param>
    void Start(string rootPath);
    
    /// <summary>
    /// Stops monitoring.
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Gets whether the service is currently monitoring.
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Raised when a new STL file is detected.
    /// </summary>
    event EventHandler<StlFileEventArgs>? FileDetected;
    
    /// <summary>
    /// Raised when the initial directory scan is complete.
    /// All existing files have been processed at this point.
    /// </summary>
    event EventHandler? ScanComplete;
}
