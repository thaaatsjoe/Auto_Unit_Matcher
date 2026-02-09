using System.Windows;

namespace AUM.UI.Views;

/// <summary>
/// Extraction failures window code-behind.
/// </summary>
public partial class ExtractionFailuresWindow : Window
{
    public ExtractionFailuresWindow()
    {
        InitializeComponent();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
