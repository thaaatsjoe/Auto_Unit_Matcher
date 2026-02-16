using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AUM.Core.Services;
using AUM.Core.Data.Repositories;
using AUM.UI.Services;
using System.Collections.ObjectModel;

namespace AUM.UI.ViewModels;

/// <summary>
/// Main application view model.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IMatchingService _matchingService;
    private readonly IUnitService _unitService;
    private readonly IAuditService _auditService;
    private readonly IUnitRepository _unitRepository;
    private readonly ISessionService _sessionService;
    
    public MainViewModel(
        IMatchingService matchingService,
        IUnitService unitService,
        IAuditService auditService,
        StlViewerViewModel stlViewerViewModel,
        IUnitRepository unitRepository,
        ISessionService sessionService)
    {
        _matchingService = matchingService;
        _unitService = unitService;
        _auditService = auditService;
        _unitRepository = unitRepository;
        _sessionService = sessionService;
        StlViewerViewModel = stlViewerViewModel;
        
        MatchResults = new ObservableCollection<MatchResultViewModel>();
        
        // Initialize operator name from session
        OperatorName = _sessionService.CurrentUser?.EmployeeName ?? "Not logged in";
        
        // Load database stats asynchronously
        _ = RefreshDatabaseStatsAsync();
    }
    
    // --- Properties ---
    
    [ObservableProperty]
    private string _statusText = "Ready";
    
    [ObservableProperty]
    private int _unitCount = 0;
    
    [ObservableProperty]
    private string _operatorName = "Not logged in";
    
    [ObservableProperty]
    private string? _currentStlPath;
    
    [ObservableProperty]
    private MatchResultViewModel? _selectedMatch;
    
    partial void OnSelectedMatchChanged(MatchResultViewModel? value)
    {
        if (value != null && !string.IsNullOrEmpty(value.StlPath))
        {
            // Update the viewer to show this specific match
            _ = StlViewerViewModel.LoadModelAsync(value.StlPath);
        }
    }
    
    [ObservableProperty]
    private bool _isLoading;
    
    public ObservableCollection<MatchResultViewModel> MatchResults { get; }
    
    /// <summary>
    /// 3D STL viewer view model for displaying scanned models.
    /// </summary>
    public StlViewerViewModel StlViewerViewModel { get; }
    
    // --- Commands ---
    
    [RelayCommand]
    private async Task ConfirmMatchAsync()
    {
        if (SelectedMatch == null) return;
        
        await _auditService.LogAsync("MATCH_CONFIRMED", SelectedMatch.CaseId, 
            $"Confirmed match with {SelectedMatch.Confidence:F1}% confidence");
        
        StatusText = $"Confirmed: Case {SelectedMatch.CaseId}";
        
        // Clear for next scan
        MatchResults.Clear();
        CurrentStlPath = null;
    }
    
    [RelayCommand]
    private async Task RejectMatchAsync()
    {
        if (SelectedMatch == null) return;
        
        await _auditService.LogAsync("MATCH_REJECTED", SelectedMatch.CaseId);
        
        StatusText = "Match rejected - awaiting next scan";
        MatchResults.Clear();
        CurrentStlPath = null;
    }
    
    [RelayCommand]
    private void OpenSettings()
    {
        // Settings window opened from App.xaml.cs or via event
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }
    
    [RelayCommand]
    private void OpenManualLookup()
    {
        // Manual lookup dialog
        ManualLookupRequested?.Invoke(this, EventArgs.Empty);
    }
    
    [RelayCommand]
    private void ShowHelp()
    {
        // Show keyboard shortcuts help
        System.Windows.MessageBox.Show(
            "Keyboard Shortcuts:\n\n" +
            "Enter - Confirm match\n" +
            "Escape - Reject match\n" +
            "S - Settings\n" +
            "M - Manual lookup\n" +
            "H or ? - Show this help",
            "Keyboard Shortcuts",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
    
    [RelayCommand]
    private async Task LoadTestStlAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*",
            Title = "Select STL File for Testing"
        };
        
        if (dialog.ShowDialog() == true)
        {
            // Trigger matching workflow
            await SearchForMatchesAsync(dialog.FileName);
            
            // Update the viewer to show the loaded file
            await StlViewerViewModel.LoadModelAsync(dialog.FileName);
        }
    }
    
    // --- Events for dialog requests ---
    public event EventHandler? SettingsRequested;
    public event EventHandler? ManualLookupRequested;
    
    // --- Methods ---
    
    /// <summary>
    /// Searches for matches for a scanned STL file.
    /// </summary>
    public async Task SearchForMatchesAsync(string stlPath)
    {
        try
        {
            IsLoading = true;
            StatusText = "Searching for matches...";
            CurrentStlPath = stlPath;
            
            var results = await _matchingService.FindMatchesFromStlAsync(stlPath, 5);
            
            // Also load into the viewer
            await StlViewerViewModel.LoadModelAsync(stlPath);
            
            
            MatchResults.Clear();
            foreach (var result in results)
            {
                MatchResults.Add(new MatchResultViewModel
                {
                    Rank = result.Rank,
                    CaseId = result.CaseId,
                    Confidence = result.Confidence,
                    StlPath = result.StlPath
                });
            }
            
            if (MatchResults.Count > 0)
            {
                SelectedMatch = MatchResults[0];
                StatusText = $"Found {MatchResults.Count} matches";
            }
            else
            {
                StatusText = "No matches found";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Refreshes database statistics (unit count).
    /// </summary>
    public async Task RefreshDatabaseStatsAsync()
    {
        try
        {
            UnitCount = await _unitRepository.CountAsync();
        }
        catch (Exception ex)
        {
            // Log error but don't fail initialization
            System.Diagnostics.Debug.WriteLine($"Error loading database stats: {ex.Message}");
        }
    }
}
