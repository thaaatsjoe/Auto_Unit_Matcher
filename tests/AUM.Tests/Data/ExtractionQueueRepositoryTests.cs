using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using Xunit;

namespace AUM.Tests.Data;

/// <summary>
/// Tests for ExtractionQueueRepository.
/// </summary>
[Trait("Category", "Database")]
public class ExtractionQueueRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseContext _context;
    private readonly ExtractionQueueRepository _repository;

    public ExtractionQueueRepositoryTests()
    {
        _context = DatabaseContext.CreateInMemory();
        _repository = new ExtractionQueueRepository(_context);
    }

    public async Task InitializeAsync()
    {
        await _context.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        _context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task EnqueueAsync_AddsToQueue()
    {
        // Arrange
        var entry = new ExtractionQueueEntry
        {
            StlPath = @"C:\Cases\test.stl",
            FileSizeBytes = 1024000,
            FileCreatedAt = DateTime.UtcNow
        };

        // Act
        var id = await _repository.EnqueueAsync(entry);
        var pending = await _repository.GetNextPendingAsync();

        // Assert
        Assert.True(id > 0);
        Assert.NotNull(pending);
        Assert.Equal(entry.StlPath, pending.StlPath);
        Assert.Equal(ExtractionStatus.Pending, pending.Status);
    }

    [Fact]
    public async Task GetNextPendingAsync_ReturnsOldestFirst()
    {
        // Arrange
        await _repository.EnqueueAsync(new ExtractionQueueEntry
        {
            StlPath = @"new.stl",
            FileSizeBytes = 100,
            FileCreatedAt = DateTime.UtcNow
        });
        await _repository.EnqueueAsync(new ExtractionQueueEntry
        {
            StlPath = @"old.stl",
            FileSizeBytes = 100,
            FileCreatedAt = DateTime.UtcNow.AddHours(-2)
        });

        // Act
        var next = await _repository.GetNextPendingAsync();

        // Assert
        Assert.Equal(@"old.stl", next!.StlPath);
    }

    [Fact]
    public async Task UpdateStatusAsync_TransitionsCorrectly()
    {
        // Arrange
        var id = await _repository.EnqueueAsync(new ExtractionQueueEntry
        {
            StlPath = @"status.stl",
            FileSizeBytes = 100,
            FileCreatedAt = DateTime.UtcNow
        });

        // Act - Transition through states
        await _repository.UpdateStatusAsync(id, ExtractionStatus.InProgress);
        var inProgress = await _repository.GetByStatusAsync(ExtractionStatus.InProgress);
        
        await _repository.UpdateStatusAsync(id, ExtractionStatus.Complete);
        var complete = await _repository.GetByStatusAsync(ExtractionStatus.Complete);

        // Assert
        Assert.Single(inProgress);
        Assert.NotNull(inProgress[0].StartedAt);
        Assert.Single(complete);
        Assert.NotNull(complete[0].CompletedAt);
    }

    [Fact]
    public async Task GetProgressAsync_CalculatesAccurately()
    {
        // Arrange
        await _repository.EnqueueAsync(new ExtractionQueueEntry { StlPath = "1.stl", FileSizeBytes = 100, FileCreatedAt = DateTime.UtcNow });
        await _repository.EnqueueAsync(new ExtractionQueueEntry { StlPath = "2.stl", FileSizeBytes = 100, FileCreatedAt = DateTime.UtcNow });
        
        var entries = await _repository.GetByStatusAsync(ExtractionStatus.Pending);
        await _repository.UpdateStatusAsync(entries[0].Id, ExtractionStatus.Complete);

        // Act
        var progress = await _repository.GetProgressAsync();

        // Assert
        Assert.Equal(1, progress.Pending);
        Assert.Equal(1, progress.Complete);
        Assert.Equal(2, progress.Total);
        Assert.Equal(50.0, progress.PercentComplete);
    }

    [Fact]
    public async Task RetryFailedAsync_ResetsToPending()
    {
        // Arrange
        var id = await _repository.EnqueueAsync(new ExtractionQueueEntry
        {
            StlPath = @"failed.stl",
            FileSizeBytes = 100,
            FileCreatedAt = DateTime.UtcNow
        });
        await _repository.UpdateStatusAsync(id, ExtractionStatus.Failed, "Test error");

        // Act
        var retried = await _repository.RetryFailedAsync();
        var entry = await _repository.GetByStatusAsync(ExtractionStatus.Pending);

        // Assert
        Assert.Equal(1, retried);
        Assert.Single(entry);
        Assert.Null(entry[0].ErrorMessage);
    }

    [Fact]
    public async Task ExistsAsync_ChecksCorrectly()
    {
        // Arrange
        await _repository.EnqueueAsync(new ExtractionQueueEntry
        {
            StlPath = @"exists.stl",
            FileSizeBytes = 100,
            FileCreatedAt = DateTime.UtcNow
        });

        // Act & Assert
        Assert.True(await _repository.ExistsAsync(@"exists.stl"));
        Assert.False(await _repository.ExistsAsync(@"notexists.stl"));
    }
}
