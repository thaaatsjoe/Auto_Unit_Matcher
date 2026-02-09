using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using Xunit;

namespace AUM.Tests.Data;

/// <summary>
/// Tests for UserRepository.
/// </summary>
[Trait("Category", "Database")]
public class UserRepositoryTests : IAsyncLifetime
{
    private readonly DatabaseContext _context;
    private readonly UserRepository _repository;

    public UserRepositoryTests()
    {
        _context = DatabaseContext.CreateInMemory();
        _repository = new UserRepository(_context);
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
    public async Task AddAsync_ReturnsId_AndCanRetrieveByEmployeeNumber()
    {
        // Arrange
        var user = new User
        {
            EmployeeNumber = "12345",
            EmployeeName = "John Smith"
        };

        // Act
        var id = await _repository.AddAsync(user);
        var retrieved = await _repository.GetByEmployeeNumberAsync("12345");

        // Assert
        Assert.True(id > 0);
        Assert.NotNull(retrieved);
        Assert.Equal("John Smith", retrieved.EmployeeName);
        Assert.True(retrieved.IsActive);
    }

    [Fact]
    public async Task GetAllActiveAsync_ExcludesDeactivatedUsers()
    {
        // Arrange
        var active = new User { EmployeeNumber = "001", EmployeeName = "Active User" };
        var inactive = new User { EmployeeNumber = "002", EmployeeName = "Inactive User", IsActive = false };
        
        await _repository.AddAsync(active);
        await _repository.AddAsync(inactive);

        // Act
        var activeUsers = await _repository.GetAllActiveAsync();

        // Assert
        Assert.Single(activeUsers);
        Assert.Equal("Active User", activeUsers[0].EmployeeName);
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse()
    {
        // Arrange
        var user = new User { EmployeeNumber = "999", EmployeeName = "To Deactivate" };
        await _repository.AddAsync(user);

        // Act
        var result = await _repository.DeactivateAsync("999");
        var retrieved = await _repository.GetByEmployeeNumberAsync("999");

        // Assert
        Assert.True(result);
        Assert.NotNull(retrieved);
        Assert.False(retrieved.IsActive);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsCorrectly()
    {
        // Arrange
        await _repository.AddAsync(new User { EmployeeNumber = "EXISTS", EmployeeName = "Exists" });

        // Act & Assert
        Assert.True(await _repository.ExistsAsync("EXISTS"));
        Assert.False(await _repository.ExistsAsync("NOT_EXISTS"));
    }

    [Fact]
    public async Task UpdateAsync_ModifiesUser()
    {
        // Arrange
        var user = new User { EmployeeNumber = "UPDATE", EmployeeName = "Original Name" };
        var id = await _repository.AddAsync(user);
        
        var toUpdate = await _repository.GetByIdAsync(id);
        toUpdate!.EmployeeName = "Updated Name";

        // Act
        await _repository.UpdateAsync(toUpdate);
        var retrieved = await _repository.GetByIdAsync(id);

        // Assert
        Assert.Equal("Updated Name", retrieved!.EmployeeName);
    }
}
