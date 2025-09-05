using System.Windows;
using System.Windows.Controls;
using APIHammerUI.ViewModels;
using System.ComponentModel;
using APIHammerUI.Models;

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

    // Collection context menu handlers
    private void CollectionMenu_SendAllRequests(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<TabCollection>(sender) is TabCollection collection && _viewModel.SendAllRequestsCommand.CanExecute(collection))
        {
            _viewModel.SendAllRequestsCommand.Execute(collection);
        }
    }

    private void CollectionMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<TabCollection>(sender) is TabCollection collection && _viewModel.RenameCollectionMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(collection))
        {
            cmd.Execute(collection);
        }
    }

    private void CollectionMenu_Export(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<TabCollection>(sender) is TabCollection collection && _viewModel.ExportCollectionMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(collection))
        {
            cmd.Execute(collection);
        }
    }

    private void CollectionMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<TabCollection>(sender) is TabCollection collection && _viewModel.DeleteCollectionMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(collection))
        {
            cmd.Execute(collection);
        }
    }

    // Tab context menu handlers
    private void TabMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<RequestTab>(sender) is RequestTab tab && _viewModel.RenameTabMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(tab))
        {
            cmd.Execute(tab);
        }
    }

    private void TabMenu_Move(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<RequestTab>(sender) is RequestTab tab && _viewModel.MoveTabMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(tab))
        {
            cmd.Execute(tab);
        }
    }

    private void TabMenu_Close(object sender, RoutedEventArgs e)
    {
        if (GetMenuDataContext<RequestTab>(sender) is RequestTab tab && _viewModel.CloseTabMenuCommand is System.Windows.Input.ICommand cmd && cmd.CanExecute(tab))
        {
            cmd.Execute(tab);
        }
    }

    private T? GetMenuDataContext<T>(object sender) where T : class
    {
        if (sender is FrameworkElement fe && fe.DataContext is T ctx)
            return ctx;
        
        if (sender is MenuItem mi)
        {
            // For safety, attempt to pull from PlacementTarget if DataContext not found
            if (mi.DataContext is T ctx2) return ctx2;
            if (mi.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pfe && pfe.DataContext is T ctx3)
                return ctx3;
        }
        return null;
    }
}