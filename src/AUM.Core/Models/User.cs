namespace AUM.Core.Models;

/// <summary>
/// Represents an operator/employee who can use the system.
/// </summary>
public class User
{
    public long Id { get; set; }

    /// <summary>Unique employee identifier used for login.</summary>
    public required string EmployeeNumber { get; set; }

    /// <summary>Display name of the employee.</summary>
    public required string EmployeeName { get; set; }

    /// <summary>Whether the user can log in. Deactivated users cannot.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>When the user account was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
