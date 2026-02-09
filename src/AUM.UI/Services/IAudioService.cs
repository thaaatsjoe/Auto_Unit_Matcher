namespace AUM.UI.Services;

/// <summary>
/// Audio feedback service for match results.
/// </summary>
public interface IAudioService
{
    /// <summary>Play success chime when match found.</summary>
    void PlaySuccess();
    
    /// <summary>Play warning tone when no match found.</summary>
    void PlayNoMatch();
    
    /// <summary>Play error alert for system errors.</summary>
    void PlayError();
    
    /// <summary>Enable or disable audio feedback.</summary>
    bool IsEnabled { get; set; }
}
