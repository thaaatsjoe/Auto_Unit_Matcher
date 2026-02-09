using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AUM.UI.Services;

namespace AUM.UI.ViewModels;

/// <summary>
/// View model for the login screen.
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly ISessionService _sessionService;
    
    public LoginViewModel(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }
    
    [ObservableProperty]
    private string _employeeNumber = string.Empty;
    
    [ObservableProperty]
    private string _statusMessage = "Please enter employee number";
    
    [ObservableProperty]
    private bool _isError;
    
    [ObservableProperty]
    private bool _isLoggingIn;
    
    /// <summary>
    /// Raised when login is successful.
    /// </summary>
    public event EventHandler? LoginSuccessful;
    
    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(EmployeeNumber))
        {
            StatusMessage = "Please enter employee number";
            IsError = true;
            return;
        }
        
        IsLoggingIn = true;
        IsError = false;
        StatusMessage = "Logging in...";
        
        try
        {
            var success = await _sessionService.LoginAsync(EmployeeNumber);
            
            if (success)
            {
                StatusMessage = $"Welcome, {_sessionService.CurrentUser?.EmployeeName}";
                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                StatusMessage = "Employee not found or inactive";
                IsError = true;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Login error: {ex.Message}";
            IsError = true;
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
}
