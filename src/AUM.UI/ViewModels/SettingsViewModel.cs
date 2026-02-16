using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;
using AUM.Core.Data.Repositories;
using AUM.Core.Services;
using System.IO;
using System.Text.Json;

namespace AUM.UI.ViewModels;

/// <summary>
/// View model for settings window.
/// Loads/saves settings to %LOCALAPPDATA%\AUM\settings.json.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IUnitRepository? _unitRepository;
    private readonly IIndexService? _indexService;
    
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AUM", "settings.json");
    
    /// <summary>
    /// Design-time constructor.
    /// </summary>
    public SettingsViewModel() : this(null, null) { }
    
    /// <summary>
    /// Runtime constructor with injected services.
    /// </summary>
    public SettingsViewModel(IUnitRepository? unitRepository, IIndexService? indexService)
    {
        _unitRepository = unitRepository;
        _indexService = indexService;
        
        // Load persisted settings
        LoadSettings();
        
        // Load live stats
        _ = LoadStatsAsync();
    }
    
    // --- Settings Properties ---
    
    [ObservableProperty]
    private string _stlRootPath = string.Empty;
    
    [ObservableProperty]
    private string _databasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AUM", "fingerprints.db");
    
    [ObservableProperty]
    private int _confidenceThreshold = 80;
    
    [ObservableProperty]
    private bool _audioEnabled = true;
    
    // --- Stats Properties (read-only display) ---
    
    [ObservableProperty]
    private int _unitCount;
    
    [ObservableProperty]
    private int _indexCount;
    
    [ObservableProperty]
    private bool _indexReady;
    
    [ObservableProperty]
    private string _statusMessage = string.Empty;
    
    [ObservableProperty]
    private bool _isBusy;
    
    /// <summary>
    /// Raised when settings are saved.
    /// </summary>
    public event EventHandler? SettingsSaved;
    
    // --- Commands ---
    
    [RelayCommand]
    private void BrowseStlPath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select STL Root Directory"
        };
        
        if (dialog.ShowDialog() == true)
        {
            StlRootPath = dialog.FolderName;
        }
    }
    
    [RelayCommand]
    private void Save()
    {
        try
        {
            var settingsDir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(settingsDir);
            
            var settings = new Dictionary<string, object>
            {
                ["StlRootPath"] = StlRootPath,
                ["ConfidenceThreshold"] = ConfidenceThreshold,
                ["AudioEnabled"] = AudioEnabled
            };
            
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(SettingsPath, json);
            
            Log.Information("Settings saved: StlRoot={Path}, Threshold={Threshold}", 
                StlRootPath, ConfidenceThreshold);
            
            StatusMessage = "Settings saved successfully";
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private async Task RebuildIndexAsync()
    {
        if (_indexService == null)
        {
            StatusMessage = "Index service not available";
            return;
        }
        
        try
        {
            IsBusy = true;
            StatusMessage = "Rebuilding index...";
            
            await _indexService.RebuildAsync();
            
            // Refresh stats after rebuild
            await LoadStatsAsync();
            
            StatusMessage = "Index rebuilt successfully";
            Log.Information("Index rebuilt via Settings UI");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Index rebuild failed");
            StatusMessage = $"Rebuild failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save Database Backup",
                Filter = "SQLite Database|*.db",
                FileName = $"aum_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db"
            };
            
            if (dialog.ShowDialog() == true)
            {
                IsBusy = true;
                StatusMessage = "Creating backup...";
                
                await Task.Run(() => File.Copy(DatabasePath, dialog.FileName, true));
                
                StatusMessage = $"Backup saved to {Path.GetFileName(dialog.FileName)}";
                Log.Information("Backup created: {Path}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup failed");
            StatusMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Restore Database Backup",
                Filter = "SQLite Database|*.db"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var result = System.Windows.MessageBox.Show(
                    "This will replace the current database. Continue?",
                    "Restore Backup",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    IsBusy = true;
                    StatusMessage = "Restoring backup...";
                    
                    await Task.Run(() => File.Copy(dialog.FileName, DatabasePath, true));
                    
                    // Refresh stats after restore
                    await LoadStatsAsync();
                    
                    StatusMessage = "Database restored — restart recommended";
                    Log.Information("Database restored from: {Path}", dialog.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
            StatusMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    // --- Private helpers ---
    
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                StlRootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return;
            }
            
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            if (root.TryGetProperty("StlRootPath", out var stlProp))
                StlRootPath = stlProp.GetString() ?? string.Empty;
            
            if (root.TryGetProperty("ConfidenceThreshold", out var threshProp))
                ConfidenceThreshold = threshProp.TryGetInt32(out var t) ? t : 80;
            
            if (root.TryGetProperty("AudioEnabled", out var audioProp))
                AudioEnabled = audioProp.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings, using defaults");
            StlRootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
    
    private async Task LoadStatsAsync()
    {
        try
        {
            if (_unitRepository != null)
                UnitCount = await _unitRepository.CountAsync();
            
            if (_indexService != null)
            {
                IndexReady = _indexService.IsReady;
                IndexCount = _indexService.Count;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load stats");
        }
    }
}
