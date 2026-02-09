using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Serilog;
using System.IO;

namespace AUM.UI.ViewModels;

/// <summary>
/// View model for settings window.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel()
    {
        // Load defaults
        StlRootPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        DatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AUM", "fingerprints.db");
        ConfidenceThreshold = 80;
        AudioEnabled = true;
    }
    
    [ObservableProperty]
    private string _stlRootPath = string.Empty;
    
    [ObservableProperty]
    private string _databasePath = string.Empty;
    
    [ObservableProperty]
    private int _confidenceThreshold;
    
    [ObservableProperty]
    private bool _audioEnabled;
    
    /// <summary>
    /// Raised when settings are saved.
    /// </summary>
    public event EventHandler? SettingsSaved;
    
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
            // TODO: Save to appsettings.json
            Log.Information("Settings saved: StlRoot={Path}, Threshold={Threshold}", 
                StlRootPath, ConfidenceThreshold);
            
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
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
                await Task.Run(() => File.Copy(DatabasePath, dialog.FileName, true));
                Log.Information("Backup created: {Path}", dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Backup failed");
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
                // Confirm with user
                var result = System.Windows.MessageBox.Show(
                    "This will replace the current database. Continue?",
                    "Restore Backup",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await Task.Run(() => File.Copy(dialog.FileName, DatabasePath, true));
                    Log.Information("Database restored from: {Path}", dialog.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Restore failed");
        }
    }
}
