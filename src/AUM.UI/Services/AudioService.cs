using NAudio.Wave;
using Serilog;
using System.IO;

namespace AUM.UI.Services;

/// <summary>
/// Audio feedback using NAudio for match results.
/// </summary>
public class AudioService : IAudioService, IDisposable
{
    private readonly string _soundsPath;
    
    public AudioService()
    {
        _soundsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
        IsEnabled = true;
    }
    
    public bool IsEnabled { get; set; }
    
    public void PlaySuccess()
    {
        if (!IsEnabled) return;
        PlaySound("success.wav", fallbackFrequency: 880, fallbackDuration: 200);
    }
    
    public void PlayNoMatch()
    {
        if (!IsEnabled) return;
        PlaySound("warning.wav", fallbackFrequency: 440, fallbackDuration: 400);
    }
    
    public void PlayError()
    {
        if (!IsEnabled) return;
        PlaySound("error.wav", fallbackFrequency: 220, fallbackDuration: 500);
    }
    
    private void PlaySound(string fileName, int fallbackFrequency, int fallbackDuration)
    {
        try
        {
            var filePath = Path.Combine(_soundsPath, fileName);
            
            if (File.Exists(filePath))
            {
                using var audioFile = new AudioFileReader(filePath);
                using var outputDevice = new WaveOutEvent();
                outputDevice.Init(audioFile);
                outputDevice.Play();
                
                // Wait for playback to complete
                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(50);
                }
            }
            else
            {
                // Fallback to system beep
                Console.Beep(fallbackFrequency, fallbackDuration);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to play sound {FileName}", fileName);
            // Silent fallback
        }
    }
    
    public void Dispose()
    {
        // No persistent resources
    }
}
