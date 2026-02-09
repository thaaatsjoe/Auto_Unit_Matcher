using System.IO;
using System.Windows;
using System.Windows.Input;
using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.DependencyInjection;
using AUM.UI.Services;
using AUM.UI.ViewModels;
using AUM.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AUM.UI;

/// <summary>
/// Main application entry point.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _serviceProvider;
    private ISessionService? _sessionService;
    
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Configure logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "aum-.log"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        Log.Information("Application starting");
        
        try
        {
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            // Initialize database and index
            await _serviceProvider.InitializeAumCoreAsync();
            
            // Get session service
            _sessionService = _serviceProvider.GetRequiredService<ISessionService>();
            _sessionService.SessionExpired += OnSessionExpired;
            _sessionService.AuthenticationChanged += OnAuthenticationChanged;
            
            // Show login window
            ShowLoginWindow();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            MessageBox.Show($"Failed to start application: {ex.Message}", 
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    
    private void ConfigureServices(IServiceCollection services)
    {
        // Paths
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUM");
        Directory.CreateDirectory(appData);
        
        var databasePath = Path.Combine(appData, "fingerprints.db");
        var indexPath = Path.Combine(appData, "index.faiss");
        
        // Add AUM.Core services
        services.AddAumCore(databasePath, indexPath);
        
        // Add UI services
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IAudioService, AudioService>();
        
        // Add ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
    }
    
    private void ShowLoginWindow()
    {
        var loginVm = _serviceProvider!.GetRequiredService<LoginViewModel>();
        var loginWindow = new LoginWindow { DataContext = loginVm };
        
        loginVm.LoginSuccessful += (s, e) =>
        {
            loginWindow.Hide();
            ShowMainWindow();
        };
        
        loginWindow.Show();
    }
    
    private void ShowMainWindow()
    {
        var mainVm = _serviceProvider!.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow { DataContext = mainVm };
        
        // Track user activity for session timeout
        mainWindow.PreviewMouseMove += (s, e) => _sessionService?.ResetInactivityTimer();
        mainWindow.PreviewKeyDown += (s, e) => _sessionService?.ResetInactivityTimer();
        
        mainWindow.Show();
        MainWindow = mainWindow;
    }
    
    private void OnSessionExpired(object? sender, EventArgs e)
    {
        Log.Information("Session expired");
        MainWindow?.Close();
        ShowLoginWindow();
    }
    
    private void OnAuthenticationChanged(object? sender, EventArgs e)
    {
        // Update main view model with current user when auth changes
        if (MainWindow?.DataContext is MainViewModel mainVm && _sessionService != null)
        {
            mainVm.OperatorName = _sessionService.CurrentUser?.EmployeeName ?? "Not logged in";
        }
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        Log.CloseAndFlush();
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        base.OnExit(e);
    }
}
