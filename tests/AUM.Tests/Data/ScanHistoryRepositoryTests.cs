using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using Xunit;

namespace AUM.Tests.Data;

/// <summary>
/// Tests for ScanHistoryRepository with focus on duplicate detection.
/// </summary>
[Trait("Category", "Database")]
public class ScanHistoryRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseContext _context;
    private readonly ScanHistoryRepository _repository;
    private readonly UnitRepository _unitRepository;

    public ScanHistoryRepositoryTests()
    {
        _context = DatabaseContext.CreateInMemory();
        _repository = new ScanHistoryRepository(_context);
        _unitRepository = new UnitRepository(_context);
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
    public async Task AddAsync_StoresScanRecord()
    {
        // Arrange
        var unitId = await _unitRepository.AddAsync(new Unit 
        { 
            CaseId = "TEST", 
            StlPath = "test.stl", 
            DescriptorBlob = new byte[] { 1 } 
        });

        var entry = new ScanHistoryEntry
        {
            EmployeeNumber = "12345",
            EmployeeName = "John Smith",
            StationId = "Station-01",
            ScannerId = "Scanner-1",
            MatchedUnitId = unitId,
            Confidence = 95.5f,
            Confirmed = true
        };

        // Act
        var id = await _repository.AddAsync(entry);
        var retrieved = await _repository.GetByIdAsync(id);

        // Assert
        Assert.True(id > 0);
        Assert.NotNull(retrieved);
        Assert.Equal("12345", retrieved.EmployeeNumber);
        Assert.Equal(95.5f, retrieved.Confidence);
        Assert.True(retrieved.Confirmed);
    }

    [Fact]
    public async Task GetRecentByUnitAsync_FindsScansWithinWindow()
    {
        // Arrange
        var unitId = await _unitRepository.AddAsync(new Unit 
        { 
            CaseId = "DUP", 
            StlPath = "dup.stl", 
            DescriptorBlob = new byte[] { 1 } 
        });

        // Add a confirmed scan for this unit
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001",
            EmployeeName = "Test",
            StationId = "S1",
            ScannerId = "Sc1",
            MatchedUnitId = unitId,
            Confirmed = true
        });

        // Act - check for duplicates within 5 hours
        var recentScans = await _repository.GetRecentByUnitAsync(unitId, TimeSpan.FromHours(5));

        // Assert
        Assert.Single(recentScans);
    }

    [Fact]
    public async Task GetStatsAsync_CalculatesCorrectly()
    {
        // Arrange
        var unitId = await _unitRepository.AddAsync(new Unit 
        { 
            CaseId = "STATS", 
            StlPath = "stats.stl", 
            DescriptorBlob = new byte[] { 1 } 
        });

        // Add various scan types
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001", EmployeeName = "A", StationId = "S", ScannerId = "Sc",
            MatchedUnitId = unitId, Confidence = 90f, Confirmed = true
        });
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001", EmployeeName = "A", StationId = "S", ScannerId = "Sc",
            MatchedUnitId = unitId, Confidence = 80f, Confirmed = true
        });
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001", EmployeeName = "A", StationId = "S", ScannerId = "Sc",
            MatchedUnitId = null, Confidence = null, Confirmed = false
        });
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001", EmployeeName = "A", StationId = "S", ScannerId = "Sc",
            MatchedUnitId = unitId, IsDuplicate = true, Confirmed = true
        });

        // Act
        var stats = await _repository.GetStatsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        // Assert
        Assert.Equal(4, stats.TotalScans);
        Assert.Equal(3, stats.ConfirmedMatches);
        Assert.Equal(1, stats.NoMatches);
        Assert.Equal(1, stats.Duplicates);
    }

    [Fact]
    public async Task GetByDateRangeAsync_FiltersCorrectly()
    {
        // Arrange
        await _repository.AddAsync(new ScanHistoryEntry
        {
            EmployeeNumber = "001", EmployeeName = "A", StationId = "S", ScannerId = "Sc"
        });

        // Act
        var today = await _repository.GetByDateRangeAsync(
            DateTime.UtcNow.AddDays(-1), 
            DateTime.UtcNow.AddDays(1));
        
        var future = await _repository.GetByDateRangeAsync(
            DateTime.UtcNow.AddDays(1), 
            DateTime.UtcNow.AddDays(2));

        // Assert
        Assert.Single(today);
        Assert.Empty(future);
    }
}
