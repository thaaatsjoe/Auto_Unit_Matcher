using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for extraction queue management.
/// </summary>
public interface IExtractionQueueRepository
{
    /// <summary>Adds a file to the extraction queue.</summary>
    Task<long> EnqueueAsync(ExtractionQueueEntry entry);
    
    /// <summary>Gets the next pending file (oldest first by file creation time).</summary>
    Task<ExtractionQueueEntry?> GetNextPendingAsync();
    
    /// <summary>Updates the status of a queue entry.</summary>
    Task<bool> UpdateStatusAsync(long id, ExtractionStatus status, string? errorMessage = null);
    
    /// <summary>Gets queue progress (count by status).</summary>
    Task<ExtractionProgress> GetProgressAsync();
    
    /// <summary>Gets all failed entries.</summary>
    Task<IReadOnlyList<ExtractionQueueEntry>> GetFailedAsync();
    
    /// <summary>Requeues failed entries (sets status back to PENDING).</summary>
    Task<int> RetryFailedAsync();
    
    /// <summary>Checks if a file is already in the queue.</summary>
    Task<bool> ExistsAsync(string stlPath);
    
    /// <summary>Gets entries by status.</summary>
    Task<IReadOnlyList<ExtractionQueueEntry>> GetByStatusAsync(ExtractionStatus status);
}

/// <summary>
/// Extraction queue progress summary.
/// </summary>
public class ExtractionProgress
{
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Complete { get; set; }
    public int Failed { get; set; }
    
    public int Total => Pending + InProgress + Complete + Failed;
    public double PercentComplete => Total == 0 ? 0 : (double)Complete / Total * 100;
}
