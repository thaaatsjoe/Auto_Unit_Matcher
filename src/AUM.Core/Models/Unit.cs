namespace AUM.Core.Models;

/// <summary>
/// Represents a dental unit with its 3D fingerprint descriptor.
/// Stored in the units table.
/// </summary>
public class Unit
{
    /// <summary>Database primary key.</summary>
    public long Id { get; set; }

    /// <summary>Case identifier from folder name, e.g., "2024-0142".</summary>
    public required string CaseId { get; set; }

    /// <summary>Full path to the original STL file.</summary>
    public required string StlPath { get; set; }

    /// <summary>Serialized 3D descriptors (FPFH), typically 50-100KB.</summary>
    public required byte[] DescriptorBlob { get; set; }

    /// <summary>When the unit was added to the database.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
