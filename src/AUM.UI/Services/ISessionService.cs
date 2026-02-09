using AUM.Core.Models;

namespace AUM.UI.Services;

/// <summary>
/// Manages user session and authentication state.
/// </summary>
public interface ISessionService
{
    /// <summary>Current logged-in user, null if not authenticated.</summary>
    User? CurrentUser { get; }
    
    /// <summary>Whether a user is currently logged in.</summary>
    bool IsAuthenticated { get; }
    
    /// <summary>Attempts to log in with an employee number.</summary>
    Task<bool> LoginAsync(string employeeNumber);
    
    /// <summary>Logs out the current user.</summary>
    void Logout();
    
    /// <summary>Resets the inactivity timer (call on user activity).</summary>
    void ResetInactivityTimer();
    
    /// <summary>Raised when session expires due to inactivity.</summary>
    event EventHandler? SessionExpired;
    
    /// <summary>Raised when user logs in or out.</summary>
    event EventHandler? AuthenticationChanged;
}
