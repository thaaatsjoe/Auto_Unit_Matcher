using System.IO;
using System.Windows;
using System.Windows.Input;
using AUM.Core.Data;
using AUM.Core.Data.Repositories;
using AUM.Core.DependencyInjection;
using AUM.Core.Services;
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
        
        // Set shutdown mode to prevent auto-exit when login window closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        
        try
        {
            // Configure services
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            
            // Initialize database and index
            await _serviceProvider.InitializeAumCoreAsync();
            
            // Check if first-run configuration is needed
            if (!IsConfigured())
            {
                ShowFirstRunSetup();
                return;
            }
            
            // Start STL monitoring if configured
            await StartStlMonitoringAsync();
            
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
        var codebookPath = Path.Combine(appData, "codebook.bin");
        
        // Add AUM.Core services
        services.AddAumCore(databasePath, indexPath, codebookPath);
        
        // Bridge Serilog to Microsoft.Extensions.Logging so ILogger<T> works in services
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        
        // Add UI services
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<IAudioService, AudioService>();
        
        // Add ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<StlViewerViewModel>();
        services.AddTransient<MainViewModel>();
    }
    
    private void ShowLoginWindow()
    {
        var loginVm = _serviceProvider!.GetRequiredService<LoginViewModel>();
        var loginWindow = new LoginWindow { DataContext = loginVm };
        
        loginVm.LoginSuccessful += (s, e) =>
        {
            try
            {
                Log.Information("Login successful - showing main window");
                loginWindow.Close();  // Close instead of Hide
                ShowMainWindow();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to show main window after login");
                MessageBox.Show($"Failed to open main window: {ex.Message}\n\nPlease check logs for details.",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        };
        
        loginWindow.Show();
    }
    
    private void ShowMainWindow()
    {
        Log.Information("Creating main window");
        var mainVm = _serviceProvider!.GetRequiredService<MainViewModel>();
        var mainWindow = new MainWindow { DataContext = mainVm };
        
        // Track user activity for session timeout
        mainWindow.PreviewMouseMove += (s, e) => _sessionService?.ResetInactivityTimer();
        mainWindow.PreviewKeyDown += (s, e) => _sessionService?.ResetInactivityTimer();
        
        Log.Information("Showing main window");
        mainWindow.Show();
        MainWindow = mainWindow;
        
        // Now that MainWindow is set, change shutdown mode
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        
        Log.Information("Main window displayed successfully");
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
    
    private async Task StartStlMonitoringAsync()
    {
        try
        {
            var stlMonitor = _serviceProvider?.GetService<IStlMonitorService>();
            
            if (stlMonitor == null)
            {
                Log.Warning("STL monitoring service not available");
                return;
            }
            
            // Load from settings file
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AUM", "settings.json");
            
            if (!File.Exists(settingsPath))
            {
                Log.Warning("Settings file not found - STL monitoring disabled");
                return;
            }
            
            var json = File.ReadAllText(settingsPath);
            Log.Debug("Raw settings.json content: {Json}", json);
            
            Dictionary<string, string>? settings;
            try
            {
                settings = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                Log.Error(jsonEx, "settings.json contains invalid JSON. Path: {SettingsPath}", settingsPath);
                MessageBox.Show(
                    $"STL root path settings file is corrupted.\n\n" +
                    $"Please reconfigure via Settings or delete:\n{settingsPath}\n\n" +
                    $"Error: {jsonEx.Message}",
                    "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (settings == null || !settings.TryGetValue("StlRootPath", out var stlRootPath))
            {
                Log.Warning("STL root path not configured in settings.json");
                return;
            }
            
            // Normalize path (trim whitespace, ensure proper UNC format)
            stlRootPath = stlRootPath.Trim();
            Log.Information("Configured STL root path: {Path}", stlRootPath);
            
            if (!Directory.Exists(stlRootPath))
            {
                Log.Warning("STL root path does not exist or is not accessible: {Path}", stlRootPath);
                MessageBox.Show(
                    $"STL root path is not accessible:\n{stlRootPath}\n\n" +
                    $"Please check the network connection and path configuration.",
                    "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Wire up event handler for new STL files
            stlMonitor.FileDetected += OnStlFileDetected;
            
            // Start monitoring
            stlMonitor.Start(stlRootPath);
            Log.Information("STL monitoring started on {Path}", stlRootPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start STL monitoring");
        }
    }
    
    private async void OnStlFileDetected(object? sender, StlFileEventArgs e)
    {
        try
        {
            Log.Information("Processing new STL: {Path}", e.FilePath);
            
            var unitService = _serviceProvider?.GetRequiredService<IUnitService>();
            
            if (unitService == null)
                return;
            
            // Check if already exists WITH a valid descriptor
            if (await unitService.ExistsAsync(e.FilePath))
            {
                var existing = await unitService.GetByStlPathAsync(e.FilePath);
                if (existing != null && existing.DescriptorBlob != null && existing.DescriptorBlob.Length > 0)
                {
                    Log.Debug("STL already registered with descriptor, skipping: {Path}", e.FilePath);
                    return;
                }
                else
                {
                    Log.Warning("STL registered but missing descriptor, re-extracting: {Path}", e.FilePath);
                    // Will re-register and overwrite the existing record
                }
            }
            
            // Register the unit (extracts FPFH descriptors)
            await unitService.RegisterUnitAsync(e.FilePath, e.CaseId);
            
            Log.Information("Successfully registered STL: {Path}", e.FilePath);
            
            // Refresh UI database count and trigger matching if on main window
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                if (MainWindow?.DataContext is MainViewModel vm)
                {
                    await vm.RefreshDatabaseStatsAsync();
                    
                    // Automatically trigger matching for the new file
                    await vm.SearchForMatchesAsync(e.FilePath);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process detected STL file: {Path}", e.FilePath);
        }
    }
    
    private bool IsConfigured()
    {
        // Check if STL root path is configured in settings file
        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUM", "settings.json");
        
        return File.Exists(settingsPath);
    }
    
    private void ShowFirstRunSetup()
    {
        Log.Information("First run detected - showing configuration wizard");
        
        var message = "Welcome to AUM!\n\n" +
                     "This appears to be your first time running the application.\n\n" +
                     "Please select the 3Shape scanner output directory where STL files are saved.\n\n" +
                     "Click OK to browse for the directory.";
        
        var result = MessageBox.Show(message, "First Run Setup", 
            MessageBoxButton.OKCancel, MessageBoxImage.Information);
        
        if (result == MessageBoxResult.OK)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select 3Shape STL Output Directory",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    var stlRootPath = dialog.SelectedPath;
                    
                    // Save to simple settings file
                    var settingsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "AUM");
                    
                    Directory.CreateDirectory(settingsDir);
                    
                    var settingsPath = Path.Combine(settingsDir, "settings.json");
                    var settings = new { StlRootPath = stlRootPath };
                    File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(settings));
                    
                    Log.Information("Configured STL root path: {Path}", stlRootPath);
                    
                    MessageBox.Show($"STL root directory configured:\n{stlRootPath}\n\nPlease restart the application.",
                        "Setup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to save settings");
                    MessageBox.Show($"Failed to save settings: {ex.Message}",
                        "Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        Shutdown(0);
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        
        // Stop STL monitoring
        var stlMonitor = _serviceProvider?.GetService<IStlMonitorService>();
        stlMonitor?.Stop();
        
        Log.CloseAndFlush();
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        base.OnExit(e);
    }
}
