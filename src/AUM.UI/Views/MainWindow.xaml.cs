using System.Windows;
using System;

namespace AUM.UI.Views;

/// <summary>
/// Main application window.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void RunV2MlTest_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "STL Files (*.stl)|*.stl",
            Title = "Select an STL Scan for V2 ONNX Testing"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                // 1. Point to the dynamically coupled ONNX binaries using Absolute MSBuild Output Paths
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string backbonePath = System.IO.Path.Combine(baseDir, "aum_backbone.onnx");
                string matcherPath = System.IO.Path.Combine(baseDir, "aum_matcher.onnx");

                using var onnxSvc = new AUM.Core.Services.OnnxInferenceService(backbonePath, matcherPath);

                // 2. Inject into Matcher, passing null (!) to V1 constraints as permitted
                var matchingSvc = new AUM.Core.Services.MatchingService(null!, null!, null, onnxSvc);

                // 3. Fire the non-destructive dynamic test
                string resultLog = matchingSvc.TestV2MlPipeline(openFileDialog.FileName);

                // 4. Validate output bounds on screen
                System.Windows.MessageBox.Show(resultLog, "V2 ML Pipeline Test Results", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Pipeline test crashed:\n{ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
