using AUM.Core.Models;

namespace AUM.Core.Data.Repositories;

/// <summary>
/// Repository for fingerprint units.
/// </summary>
public interface IUnitRepository : IRepository<Unit>
{
    /// <summary>Gets a unit by its STL file path.</summary>
    Task<Unit?> GetByStlPathAsync(string stlPath);
    
    /// <summary>Gets all units for a case ID.</summary>
    Task<IReadOnlyList<Unit>> GetByCaseIdAsync(string caseId);
    
    /// <summary>Checks if a unit exists for the given STL path.</summary>
    Task<bool> ExistsAsync(string stlPath);
    
    /// <summary>Gets all unit IDs and descriptor blobs for index rebuilding.</summary>
    Task<IReadOnlyList<(long Id, byte[] DescriptorBlob)>> GetAllDescriptorsAsync();
    
    /// <summary>Inserts a new unit or updates existing one if STL path already exists. Returns the unit ID.</summary>
    Task<long> UpsertByStlPathAsync(Unit entity);
}
