using AUM.Core.Models;

namespace AUM.Core.Services;

/// <summary>
/// Service for managing fingerprint units.
/// </summary>
public interface IUnitService
{
    /// <summary>
    /// Registers a new unit from an STL file.
    /// </summary>
    /// <param name="stlPath">Path to the STL file.</param>
    /// <param name="caseId">Case ID (from folder name).</param>
    /// <returns>The database ID of the registered unit.</returns>
    Task<long> RegisterUnitAsync(string stlPath, string caseId);
    
    /// <summary>
    /// Gets a unit by its database ID.
    /// </summary>
    Task<Unit?> GetByIdAsync(long id);
    
    /// <summary>
    /// Gets all units for a case ID.
    /// </summary>
    Task<IEnumerable<Unit>> GetByCaseIdAsync(string caseId);
    
    /// <summary>
    /// Checks if a unit with the given STL path already exists.
    /// </summary>
    Task<bool> ExistsAsync(string stlPath);
    
}
