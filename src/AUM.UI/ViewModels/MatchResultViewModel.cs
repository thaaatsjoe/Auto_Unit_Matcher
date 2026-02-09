using CommunityToolkit.Mvvm.ComponentModel;

namespace AUM.UI.ViewModels;

/// <summary>
/// View model for a single match result in the TOP 5 list.
/// </summary>
public partial class MatchResultViewModel : ObservableObject
{
    [ObservableProperty]
    private int _rank;
    
    [ObservableProperty]
    private string _caseId = string.Empty;
    
    [ObservableProperty]
    private float _confidence;
    
    [ObservableProperty]
    private string _stlPath = string.Empty;
    
    /// <summary>
    /// Display string for the match (e.g., "1. Case-123 (97.2%)")
    /// </summary>
    public string DisplayText => $"{Rank}. {CaseId} ({Confidence:F1}%)";
}
