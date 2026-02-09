using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AUM.Core.Services;
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
    
    public MainViewModel(
        IMatchingService matchingService,
        IUnitService unitService,
        IAuditService auditService)
    {
        _matchingService = matchingService;
        _unitService = unitService;
        _auditService = auditService;
        
        MatchResults = new ObservableCollection<MatchResultViewModel>();
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
    
    [ObservableProperty]
    private bool _isLoading;
    
    public ObservableCollection<MatchResultViewModel> MatchResults { get; }
    
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
}
