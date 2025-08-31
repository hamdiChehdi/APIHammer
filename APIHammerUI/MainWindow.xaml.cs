using System.Windows;
using System.Windows.Controls;
using APIHammerUI.ViewModels;
using System.ComponentModel;

namespace APIHammerUI;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        
        // Handle window closing to save data
        Closing += MainWindow_Closing;
        
        // Initialize data when window is loaded
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Start the data initialization
        await _viewModel.InitializeAsync();
        ExpandFirstCollection();
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // Cancel the close to perform async shutdown
        e.Cancel = true;
        
        try
        {
            // Disable the window to prevent user interaction
            this.IsEnabled = false;
            
            // Perform shutdown operations
            await _viewModel.ShutdownAsync();
            
            // Now actually close the window
            Application.Current.Shutdown();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex}");
            // Force close even if there's an error
            Application.Current.Shutdown();
        }
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.HandleTreeViewSelectionChanged(e.NewValue);
    }

    private void ExpandFirstCollection()
    {
        if (TabExplorerTreeView.Items.Count > 0)
        {
            var firstItem = TabExplorerTreeView.ItemContainerGenerator.ContainerFromIndex(0) as TreeViewItem;
            if (firstItem != null)
            {
                firstItem.IsExpanded = true;
            }
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // This is now handled by MainWindow_Loaded above
    }
}