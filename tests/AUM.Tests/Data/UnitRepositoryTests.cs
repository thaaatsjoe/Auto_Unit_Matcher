using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using Xunit;

namespace AUM.Tests.Data;

/// <summary>
/// Tests for UnitRepository.
/// </summary>
[Trait("Category", "Database")]
public class UnitRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseContext _context;
    private readonly UnitRepository _repository;

    public UnitRepositoryTests()
    {
        _context = DatabaseContext.CreateInMemory();
        _repository = new UnitRepository(_context);
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
    public async Task AddAsync_ReturnsId_AndCanRetrieveById()
    {
        // Arrange
        var unit = new Unit
        {
            CaseId = "2024-0142",
            StlPath = @"C:\Cases\2024-0142\crown_14.stl",
            DescriptorBlob = new byte[] { 1, 2, 3, 4, 5 }
        };

        // Act
        var id = await _repository.AddAsync(unit);
        var retrieved = await _repository.GetByIdAsync(id);

        // Assert
        Assert.True(id > 0);
        Assert.NotNull(retrieved);
        Assert.Equal("2024-0142", retrieved.CaseId);
        Assert.Equal(unit.StlPath, retrieved.StlPath);
        Assert.Equal(unit.DescriptorBlob, retrieved.DescriptorBlob);
    }

    [Fact]
    public async Task GetByStlPathAsync_ReturnsUnit_WhenExists()
    {
        // Arrange
        var unit = new Unit
        {
            CaseId = "2024-0143",
            StlPath = @"C:\Cases\2024-0143\bridge.stl",
            DescriptorBlob = new byte[] { 10, 20, 30 }
        };
        await _repository.AddAsync(unit);

        // Act
        var retrieved = await _repository.GetByStlPathAsync(unit.StlPath);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("2024-0143", retrieved.CaseId);
    }

    [Fact]
    public async Task GetByCaseIdAsync_ReturnsMultipleUnits()
    {
        // Arrange
        var unit1 = new Unit { CaseId = "2024-0144", StlPath = @"C:\Cases\crown1.stl", DescriptorBlob = new byte[] { 1 } };
        var unit2 = new Unit { CaseId = "2024-0144", StlPath = @"C:\Cases\crown2.stl", DescriptorBlob = new byte[] { 2 } };
        var unit3 = new Unit { CaseId = "2024-0145", StlPath = @"C:\Cases\other.stl", DescriptorBlob = new byte[] { 3 } };
        
        await _repository.AddAsync(unit1);
        await _repository.AddAsync(unit2);
        await _repository.AddAsync(unit3);

        // Act
        var results = await _repository.GetByCaseIdAsync("2024-0144");

        // Assert
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenPathExists()
    {
        // Arrange
        var unit = new Unit { CaseId = "test", StlPath = @"C:\test.stl", DescriptorBlob = new byte[] { 1 } };
        await _repository.AddAsync(unit);

        // Act
        var exists = await _repository.ExistsAsync(@"C:\test.stl");
        var notExists = await _repository.ExistsAsync(@"C:\nonexistent.stl");

        // Assert
        Assert.True(exists);
        Assert.False(notExists);
    }

    [Fact]
    public async Task GetAllDescriptorsAsync_ReturnsIdAndBlob()
    {
        // Arrange
        var blob1 = new byte[] { 1, 2, 3 };
        var blob2 = new byte[] { 4, 5, 6 };
        await _repository.AddAsync(new Unit { CaseId = "A", StlPath = @"a.stl", DescriptorBlob = blob1 });
        await _repository.AddAsync(new Unit { CaseId = "B", StlPath = @"b.stl", DescriptorBlob = blob2 });

        // Act
        var descriptors = await _repository.GetAllDescriptorsAsync();

        // Assert
        Assert.Equal(2, descriptors.Count);
        Assert.All(descriptors, d => Assert.True(d.Id > 0));
        Assert.All(descriptors, d => Assert.NotEmpty(d.DescriptorBlob));
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnit()
    {
        // Arrange
        var unit = new Unit { CaseId = "delete-test", StlPath = @"delete.stl", DescriptorBlob = new byte[] { 1 } };
        var id = await _repository.AddAsync(unit);

        // Act
        var deleted = await _repository.DeleteAsync(id);
        var retrieved = await _repository.GetByIdAsync(id);

        // Assert
        Assert.True(deleted);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task CountAsync_ReturnsCorrectCount()
    {
        // Arrange
        await _repository.AddAsync(new Unit { CaseId = "1", StlPath = @"1.stl", DescriptorBlob = new byte[] { 1 } });
        await _repository.AddAsync(new Unit { CaseId = "2", StlPath = @"2.stl", DescriptorBlob = new byte[] { 2 } });
        await _repository.AddAsync(new Unit { CaseId = "3", StlPath = @"3.stl", DescriptorBlob = new byte[] { 3 } });

        // Act
        var count = await _repository.CountAsync();

        // Assert
        Assert.Equal(3, count);
    }
}
