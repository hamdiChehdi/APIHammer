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
    private ObservableCollection<RequestTab> _tabs = new();
    private RequestTab? _selectedTab;

    public ObservableCollection<RequestTab> Tabs
    {
        get => _tabs;
        set
        {
            _tabs = value;
            OnPropertyChanged(nameof(Tabs));
            OnPropertyChanged(nameof(HasNoTabs));
        }
    }

    public RequestTab? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab != null)
                _selectedTab.IsSelected = false;

            _selectedTab = value;
            
            if (_selectedTab != null)
                _selectedTab.IsSelected = true;

            OnPropertyChanged(nameof(SelectedTab));
        }
    }

    public bool HasNoTabs => !Tabs.Any();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        // Subscribe to collection changes to update HasNoTabs property
        Tabs.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasNoTabs));

        // Handle double-click on tab explorer items
        TabExplorer.MouseDoubleClick += TabExplorer_MouseDoubleClick;

        // Handle right-click on tab explorer items for renaming
        TabExplorer.MouseRightButtonDown += TabExplorer_MouseRightButtonDown;

        // Create a default HTTP tab
        CreateNewTab(RequestType.HTTP, "New HTTP Request");
    }

    private void TabExplorer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TabExplorer.SelectedItem is RequestTab selectedTab)
        {
            SelectedTab = selectedTab;
        }
    }

    private void TabExplorer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Find the ListBoxItem that was clicked
        var listBoxItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
        if (listBoxItem?.DataContext is RequestTab requestTab)
        {
            RenameTab(requestTab);
            e.Handled = true;
        }
    }

    private void NewHttpTab_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTab(RequestType.HTTP, "New HTTP Request");
    }

    private void NewWebSocketTab_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTab(RequestType.WebSocket, "New WebSocket");
    }

    private void NewGrpcTab_Click(object sender, RoutedEventArgs e)
    {
        CreateNewTab(RequestType.gRPC, "New gRPC Request");
    }

    private void CreateNewTab(RequestType requestType, string defaultName)
    {
        var tab = new RequestTab
        {
            Name = defaultName,
            RequestType = requestType
        };

        // Create the appropriate content based on request type
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

        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is RequestTab tab)
        {
            CloseTab(tab);
        }
    }

    private void RenameTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is RequestTab tab)
        {
            RenameTab(tab);
        }
    }

    private void CloseTabMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.Tag is RequestTab tab)
        {
            CloseTab(tab);
        }
    }

    private void CloseTab(RequestTab tab)
    {
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Select adjacent tab if available
        if (Tabs.Any())
        {
            if (index >= Tabs.Count)
                index = Tabs.Count - 1;
            SelectedTab = Tabs[index];
        }
        else
        {
            SelectedTab = null;
        }
    }

    protected override void OnPreviewMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // Handle right-click on traditional tab headers for renaming
        if (e.Source is FrameworkElement element)
        {
            var tab = FindAncestor<TabItem>(element);
            if (tab?.DataContext is RequestTab requestTab)
            {
                RenameTab(requestTab);
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewMouseRightButtonDown(e);
    }

    private void RenameTab(RequestTab tab)
    {
        try
        {
            var dialog = new RenameTabDialog(tab.Name)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                tab.Name = dialog.TabName;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening rename dialog: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static T? FindAncestor<T>(DependencyObject? dependencyObject) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(dependencyObject);

        if (parent == null) return null;

        var parentT = parent as T;
        return parentT ?? FindAncestor<T>(parent);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}