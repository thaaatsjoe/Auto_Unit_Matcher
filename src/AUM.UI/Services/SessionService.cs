using AUM.Core.Data.Repositories;
using AUM.Core.Models;
using AUM.Core.Services;

namespace AUM.UI.Services;

/// <summary>
/// Manages user session with 10-minute inactivity timeout.
/// </summary>
public class SessionService : ISessionService, IDisposable
{
    private readonly IUserRepository _userRepository;
    private readonly IAuditService _auditService;
    private readonly System.Timers.Timer _inactivityTimer;
    private readonly TimeSpan _timeoutDuration = TimeSpan.FromMinutes(10);
    
    public SessionService(IUserRepository userRepository, IAuditService auditService)
    {
        _userRepository = userRepository;
        _auditService = auditService;
        
        _inactivityTimer = new System.Timers.Timer(_timeoutDuration.TotalMilliseconds);
        _inactivityTimer.Elapsed += OnInactivityTimeout;
        _inactivityTimer.AutoReset = false;
    }
    
    public User? CurrentUser { get; private set; }
    
    public bool IsAuthenticated => CurrentUser != null;
    
    public event EventHandler? SessionExpired;
    public event EventHandler? AuthenticationChanged;
    
    public async Task<bool> LoginAsync(string employeeNumber)
    {
        if (string.IsNullOrWhiteSpace(employeeNumber))
            return false;
        
        var user = await _userRepository.GetByEmployeeNumberAsync(employeeNumber);
        
        if (user == null || !user.IsActive)
            return false;
        
        CurrentUser = user;
        
        // Log the login
        if (_auditService is AuditService auditSvc)
        {
            auditSvc.SetContext(user.EmployeeNumber, user.EmployeeName, Environment.MachineName);
        }
        await _auditService.LogAsync("LOGIN");
        
        // Start inactivity timer
        _inactivityTimer.Start();
        
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
    
    public void Logout()
    {
        if (CurrentUser != null)
        {
            // Fire and forget the audit log
            _ = _auditService.LogAsync("LOGOUT");
        }
        
        CurrentUser = null;
        _inactivityTimer.Stop();
        
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public void ResetInactivityTimer()
    {
        if (IsAuthenticated)
        {
            _inactivityTimer.Stop();
            _inactivityTimer.Start();
        }
    }
    
    private void OnInactivityTimeout(object? sender, System.Timers.ElapsedEventArgs e)
    {
        // Session expired - fire event on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Logout();
            SessionExpired?.Invoke(this, EventArgs.Empty);
        });
    }
    
    public void Dispose()
    {
        _inactivityTimer.Dispose();
    }
}
