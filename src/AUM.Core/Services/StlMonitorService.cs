using Microsoft.Extensions.Logging;

namespace AUM.Core.Services;

/// <summary>
/// Service for monitoring directories for new STL files.
/// Uses FileSystemWatcher to detect new files.
/// </summary>
public class StlMonitorService : IStlMonitorService
{
    private readonly ILogger<StlMonitorService>? _logger;
    private FileSystemWatcher? _watcher;
    private bool _disposed;
    
    public StlMonitorService(ILogger<StlMonitorService>? logger = null)
    {
        _logger = logger;
    }
    
    /// <inheritdoc/>
    public bool IsRunning => _watcher?.EnableRaisingEvents ?? false;
    
    /// <inheritdoc/>
    public event EventHandler<StlFileEventArgs>? FileDetected;
    
    /// <inheritdoc/>
    public event EventHandler? ScanComplete;
    
    /// <inheritdoc/>
    public void Start(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("Root path cannot be null or empty", nameof(rootPath));
        
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException($"Directory not found: {rootPath}");
        
        Stop(); // Stop any existing watcher
        
        _watcher = new FileSystemWatcher(rootPath)
        {
            Filter = "*.stl",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime
        };
        
        _watcher.Created += OnFileCreated;
        _watcher.EnableRaisingEvents = true;
        
        _logger?.LogInformation("Started monitoring {Path} for STL files", rootPath);
        
        // Process existing files in the directory
        ScanExistingFiles(rootPath);
    }
    
    /// <inheritdoc/>
    public void Stop()
    {
        if (_watcher != null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Created -= OnFileCreated;
            _watcher.Dispose();
            _watcher = null;
            
            _logger?.LogInformation("Stopped STL monitoring");
        }
    }
    
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Parse case ID from path (parent folder name)
            var caseId = ParseCaseId(e.FullPath);
            
            _logger?.LogInformation("Detected new STL file: {Path} (Case: {CaseId})", e.FullPath, caseId);
            
            FileDetected?.Invoke(this, new StlFileEventArgs
            {
                FilePath = e.FullPath,
                CaseId = caseId
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing detected file: {Path}", e.FullPath);
        }
    }
    
    /// <summary>
    /// Scans the root path for existing STL files and raises FileDetected events for each.
    /// This processes the initial database population.
    /// </summary>
    private void ScanExistingFiles(string rootPath)
    {
        try
        {
            _logger?.LogInformation("Scanning directory for existing STL files: {Path}", rootPath);
            
            var files = Directory.GetFiles(rootPath, "*.stl", SearchOption.AllDirectories);
            
            _logger?.LogInformation("Found {Count} existing STL files to process", files.Length);
            
            foreach (var filePath in files)
            {
                try
                {
                    var caseId = ParseCaseId(filePath);
                    
                    _logger?.LogDebug("Processing existing file: {Path} (Case: {CaseId})", filePath, caseId);
                    
                    // Raise same event as newly created files
                    FileDetected?.Invoke(this, new StlFileEventArgs
                    {
                        FilePath = filePath,
                        CaseId = caseId
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to process existing file: {Path}", filePath);
                }
            }
            
            _logger?.LogInformation("Completed initial scan of existing files");
            
            // Signal that initial scan is done — index can now be trained
            ScanComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to scan existing files in {Path}", rootPath);
        }
    }
    
    /// <summary>
    /// Parses the case ID from the file path.
    /// Assumes case ID is the parent folder name.
    /// </summary>
    private static string ParseCaseId(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir))
            return "UNKNOWN";
        
        return Path.GetFileName(dir) ?? "UNKNOWN";
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        
        Stop();
        _disposed = true;
    }
}
