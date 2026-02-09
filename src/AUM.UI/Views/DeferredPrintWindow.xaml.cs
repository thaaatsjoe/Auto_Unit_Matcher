using System.Windows;

namespace AUM.UI.Views;

/// <summary>
/// Deferred print queue window code-behind.
/// </summary>
public partial class DeferredPrintWindow : Window
{
    public DeferredPrintWindow()
    {
        InitializeComponent();
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
