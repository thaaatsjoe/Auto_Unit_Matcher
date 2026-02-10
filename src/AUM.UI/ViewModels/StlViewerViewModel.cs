using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.IO;
using HelixToolkit.Wpf;
using Serilog;

namespace AUM.UI.ViewModels;

/// <summary>
/// View model for the 3D STL viewer (using standard HelixToolkit.Wpf).
/// </summary>
public partial class StlViewerViewModel : ObservableObject
{
    public StlViewerViewModel()
    {
        // Default material - light gray
        ModelMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(200, 200, 210)));
    }
    
    [ObservableProperty]
    private Model3D? _model;
    
    [ObservableProperty]
    private Material? _modelMaterial;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private bool _hasModel;
    
    [ObservableProperty]
    private string? _currentPath;
    
    /// <summary>
    /// Loads an STL file into the viewer.
    /// </summary>
    public async Task LoadModelAsync(string stlPath)
    {
        if (string.IsNullOrEmpty(stlPath) || !File.Exists(stlPath))
        {
            Log.Warning("STL file not found: {Path}", stlPath);
            return;
        }
        
        IsLoading = true;
        HasModel = false;
        
        try
        {
            // Load on background thread
            var model = await Task.Run(() => LoadStlModel(stlPath));
            
            Model = model;
            CurrentPath = stlPath;
            HasModel = true;
            
            Log.Information("Loaded STL: {Path}", stlPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load STL: {Path}", stlPath);
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private Model3D LoadStlModel(string stlPath)
    {
        var reader = new StLReader();
        var model = reader.Read(stlPath);
        
        if (model == null)
            throw new InvalidOperationException("Failed to load STL file");
            
        // Freeze to allow cross-thread access (Must create DependencySource on same Thread...)
        model.Freeze();
        
        return model;
    }
    
    /// <summary>
    /// Clears the current model.
    /// </summary>
    public void ClearModel()
    {
        Model = null;
        CurrentPath = null;
        HasModel = false;
    }
}
