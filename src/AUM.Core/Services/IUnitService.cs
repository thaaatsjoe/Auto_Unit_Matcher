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
    /// Gets a unit by its unique ID.
    /// </summary>
    /// <param name="id">The unit ID.</param>
    /// <returns>The unit if found, otherwise null.</returns>
    Task<Unit?> GetByIdAsync(long id);
    
    /// <summary>
    /// Gets a unit by its STL file path.
    /// </summary>
    /// <param name="stlPath">The STL file path.</param>
    /// <returns>The unit if found, otherwise null.</returns>
    Task<Unit?> GetByStlPathAsync(string stlPath);
    
    /// <summary>
    /// Gets all units for a specific case ID.
    /// </summary>
    /// <param name="caseId">The case ID.</param>
    /// <returns>Collection of units for the case.</returns>
    Task<IEnumerable<Unit>> GetByCaseIdAsync(string caseId);
    
    /// <summary>
    /// Checks if a unit with the given STL path already exists.
    /// </summary>
    /// <param name="stlPath">The STL file path.</param>
    /// <returns>True if the unit exists, otherwise false.</returns>
    Task<bool> ExistsAsync(string stlPath);
    
}
