using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using APIHammerUI.Models;
using APIHammerUI.Views;

namespace APIHammerUI;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private ObservableCollection<TabCollection> _tabCollections = new();
    private TabCollection? _selectedCollection;
    private RequestTab? _selectedTab;

    public ObservableCollection<TabCollection> TabCollections
    {
        get => _tabCollections;
        set
        {
            _tabCollections = value;
            OnPropertyChanged(nameof(TabCollections));
        }
    }

    public TabCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            _selectedCollection = value;
            OnPropertyChanged(nameof(SelectedCollection));
        }
    }

    public RequestTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            _selectedTab = value;
            OnPropertyChanged(nameof(SelectedTab));
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Initialize default collection with a sample tab
        var defaultCollection = new TabCollection { Name = "Default Collection" };
        
        // Add a sample HTTP request
        var sampleHttpTab = new RequestTab
        {
            Name = "Sample HTTP Request",
            RequestType = RequestType.HTTP
        };
        var httpRequest = new HttpRequest();
        var httpView = new HttpRequestView { DataContext = httpRequest };
        sampleHttpTab.Content = httpView;
        defaultCollection.Tabs.Add(sampleHttpTab);

        TabCollections.Add(defaultCollection);
        SelectedCollection = defaultCollection;
        
        // Expand the first collection by default
        Loaded += (s, e) => ExpandFirstCollection();
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

    private void CreateNewCollection_Click(object sender, RoutedEventArgs e)
    {
        var newCollection = new TabCollection { Name = "New Collection" };
        TabCollections.Add(newCollection);
        SelectedCollection = newCollection;
    }

    private void RenameCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCollection == null) return;

        try
        {
            var dialog = new RenameTabDialog(SelectedCollection.Name)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedCollection.Name = dialog.TabName;
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Error renaming collection: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteCollection_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCollection == null) return;

        var result = MessageBox.Show($"Are you sure you want to delete '{SelectedCollection.Name}' and all its tabs?", 
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            TabCollections.Remove(SelectedCollection);
            SelectedCollection = TabCollections.FirstOrDefault();
            SelectedTab = null;
        }
    }

    private void AddTabToCollection(RequestType requestType, string defaultName)
    {
        if (SelectedCollection == null) 
        {
            // Create a default collection if none exists
            var defaultCollection = new TabCollection { Name = "Default Collection" };
            TabCollections.Add(defaultCollection);
            SelectedCollection = defaultCollection;
        }

        var tab = new RequestTab
        {
            Name = defaultName,
            RequestType = requestType
        };

        switch (requestType)
        {
            case RequestType.HTTP:
                var httpRequest = new HttpRequest();
                var httpView = new HttpRequestView { DataContext = httpRequest };
                tab.Content = httpView;
                break;

            case RequestType.WebSocket:
                var wsRequest = new WebSocketRequest();
                var wsView = new WebSocketRequestView { DataContext = wsRequest };
                tab.Content = wsView;
                break;

            case RequestType.gRPC:
                var grpcRequest = new GrpcRequest();
                var grpcView = new GrpcRequestView { DataContext = grpcRequest };
                tab.Content = grpcView;
                break;
        }

        SelectedCollection.Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void NewHttpTab_Click(object sender, RoutedEventArgs e)
    {
        AddTabToCollection(RequestType.HTTP, "New HTTP Request");
    }

    private void NewWebSocketTab_Click(object sender, RoutedEventArgs e)
    {
        AddTabToCollection(RequestType.WebSocket, "New WebSocket");
    }

    private void NewGrpcTab_Click(object sender, RoutedEventArgs e)
    {
        AddTabToCollection(RequestType.gRPC, "New gRPC Request");
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TabCollection collection)
        {
            SelectedCollection = collection;
            // Don't change selected tab when selecting collection
        }
        else if (e.NewValue is RequestTab tab)
        {
            SelectedTab = tab;
            // Find the collection that contains this tab
            var parentCollection = TabCollections.FirstOrDefault(c => c.Tabs.Contains(tab));
            if (parentCollection != null)
            {
                SelectedCollection = parentCollection;
            }
        }
    }

    private void RenameTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab == null) return;

        try
        {
            var dialog = new RenameTabDialog(SelectedTab.Name)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedTab.Name = dialog.TabName;
            }
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"{ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab == null || SelectedCollection == null) return;

        SelectedCollection.Tabs.Remove(SelectedTab);
        SelectedTab = SelectedCollection.Tabs.FirstOrDefault();
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RequestTab tab && SelectedCollection != null)
        {
            SelectedCollection.Tabs.Remove(tab);
            if (SelectedTab == tab)
            {
                SelectedTab = SelectedCollection.Tabs.FirstOrDefault();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}