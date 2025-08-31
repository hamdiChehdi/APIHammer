using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using APIHammerUI.Models;
using APIHammerUI.Views;
using APIHammerUI.Services;
using APIHammerUI.Commands;
using System.Text;
using System.Runtime.CompilerServices;

namespace APIHammerUI.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly DatabaseService _databaseService;
    private readonly PersistenceManagerService _persistenceManager;
    private ObservableCollection<TabCollection> _tabCollections = new();
    private TabCollection? _selectedCollection;
    private RequestTab? _selectedTab;
    private bool _isLoading;

    public ObservableCollection<TabCollection> TabCollections
    {
        get => _tabCollections;
        set => SetProperty(ref _tabCollections, value);
    }

    public TabCollection? SelectedCollection
    {
        get => _selectedCollection;
        set => SetProperty(ref _selectedCollection, value);
    }

    public RequestTab? SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    // Commands
    public ICommand CreateNewCollectionCommand { get; }
    public ICommand RenameCollectionCommand { get; }
    public ICommand DeleteCollectionCommand { get; }
    public ICommand SendAllRequestsCommand { get; }
    public ICommand RenameCollectionMenuCommand { get; }
    public ICommand ExportCollectionMenuCommand { get; }
    public ICommand DeleteCollectionMenuCommand { get; }
    public ICommand NewHttpTabCommand { get; }
    public ICommand NewWebSocketTabCommand { get; }
    public ICommand NewGrpcTabCommand { get; }
    public ICommand RenameTabMenuCommand { get; }
    public ICommand MoveTabMenuCommand { get; }
    public ICommand CloseTabMenuCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ImportOpenApiCommand { get; }

    public MainWindowViewModel()
    {
        // Initialize services
        _databaseService = new DatabaseService();
        _persistenceManager = new PersistenceManagerService(_databaseService);

        // Subscribe to auto-save events
        _persistenceManager.AutoSaveRequested += OnAutoSaveRequested;

        // Initialize commands
        CreateNewCollectionCommand = new RelayCommand(ExecuteCreateNewCollection);
        RenameCollectionCommand = new RelayCommand(ExecuteRenameCollection, CanRenameCollection);
        DeleteCollectionCommand = new RelayCommand(ExecuteDeleteCollection, CanDeleteCollection);
        SendAllRequestsCommand = new RelayCommand<TabCollection>(async (collection) => await ExecuteSendAllRequestsAsync(collection));
        RenameCollectionMenuCommand = new RelayCommand<TabCollection>(ExecuteRenameCollectionMenu);
        ExportCollectionMenuCommand = new RelayCommand<TabCollection>(ExecuteExportCollectionMenu);
        DeleteCollectionMenuCommand = new RelayCommand<TabCollection>(ExecuteDeleteCollectionMenu);
        NewHttpTabCommand = new RelayCommand(ExecuteNewHttpTab);
        NewWebSocketTabCommand = new RelayCommand(ExecuteNewWebSocketTab);
        NewGrpcTabCommand = new RelayCommand(ExecuteNewGrpcTab);
        RenameTabMenuCommand = new RelayCommand<RequestTab>(ExecuteRenameTabMenu, CanExecuteRenameTabMenu);
        MoveTabMenuCommand = new RelayCommand<RequestTab>(ExecuteMoveTabMenu, CanExecuteMoveTabMenu);
        CloseTabMenuCommand = new RelayCommand<RequestTab>(ExecuteCloseTabMenu, CanExecuteCloseTabMenu);
        CloseTabCommand = new RelayCommand<RequestTab>(ExecuteCloseTab);
        ImportOpenApiCommand = new RelayCommand(ExecuteImportOpenApi);

        // Subscribe to property changes for command updates
        PropertyChanged += OnPropertyChanged;
    }

    /// <summary>
    /// Initialize the ViewModel data (should be called after UI is loaded)
    /// </summary>
    public async Task InitializeAsync()
    {
        await InitializeDataAsync();
    }

    private async Task InitializeDataAsync()
    {
        try
        {
            IsLoading = true;
            
            // Load collections from database
            var loadedCollections = await _persistenceManager.LoadCollectionsAsync();
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TabCollections = loadedCollections;
                SelectedCollection = TabCollections.FirstOrDefault();
                
                // Enable auto-save after loading
                _persistenceManager.EnableAutoSave();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading data: {ex}");
            
            // Create default collection on error
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var defaultCollection = new TabCollection { Name = "Default Collection" };
                TabCollections.Add(defaultCollection);
                SelectedCollection = defaultCollection;
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void OnAutoSaveRequested()
    {
        await SaveAllDataAsync();
    }

    private async Task SaveAllDataAsync()
    {
        try
        {
            // Check if services are disposed before attempting to save
            if (_persistenceManager == null || _databaseService == null)
                return;
                
            await _persistenceManager.SaveCollectionsAsync(TabCollections);
        }
        catch (ObjectDisposedException)
        {
            // Service was disposed during shutdown - this is expected and safe to ignore
            System.Diagnostics.Debug.WriteLine("SaveAllDataAsync: Database service was disposed during shutdown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during auto-save: {ex}");
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectedCollection))
        {
            (RenameCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCollectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private async void ExecuteCreateNewCollection()
    {
        var newCollection = new TabCollection { Name = "New Collection" };
        TabCollections.Add(newCollection);
        SelectedCollection = newCollection;
        
        // Save immediately
        await _persistenceManager.SaveCollectionAsync(newCollection);
    }

    private void ExecuteRenameCollection()
    {
        if (SelectedCollection == null) return;
        ExecuteRenameCollectionMenu(SelectedCollection);
    }

    private bool CanRenameCollection()
    {
        return SelectedCollection != null;
    }

    private async void ExecuteDeleteCollection()
    {
        if (SelectedCollection == null) return;
        await ExecuteDeleteCollectionMenuAsync(SelectedCollection);
    }

    private bool CanDeleteCollection()
    {
        return SelectedCollection != null;
    }

    private bool CanExecuteRenameTabMenu(RequestTab? tab) => tab != null;
    private bool CanExecuteMoveTabMenu(RequestTab? tab) => tab != null;
    private bool CanExecuteCloseTabMenu(RequestTab? tab) => tab != null;

    private async Task ExecuteSendAllRequestsAsync(TabCollection? collection)
    {
        if (collection == null) return;

        try
        {
            // Count HTTP requests in the collection
            var httpRequestCount = collection.Tabs.Count(tab => 
                tab.RequestType == RequestType.HTTP && 
                tab.Content is HttpRequestView httpView && 
                httpView.DataContext is HttpRequestViewModel viewModel &&
                !string.IsNullOrWhiteSpace(viewModel.HttpRequest.Url));

            if (httpRequestCount == 0)
            {
                MessageBox.Show($"Collection '{collection.Name}' contains no valid HTTP requests to send.", 
                    "No Requests", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Confirm the action
            var confirmResult = MessageBox.Show(
                $"Send all {httpRequestCount} HTTP requests in collection '{collection.Name}'?\n\n" +
                "This will execute all requests concurrently and update their responses.",
                "Confirm Batch Request",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
                return;

            // Show progress dialog
            var progressDialog = new BatchRequestProgressDialog(collection)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var dialogResult = progressDialog.ShowDialog();
            
            // Show summary when complete
            if (dialogResult == true && progressDialog.Result != null)
            {
                ShowBatchRequestSummary(progressDialog.Result, collection.Name);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error executing batch requests: {ex.Message}", "Batch Request Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowBatchRequestSummary(BatchRequestResult result, string collectionName)
    {
        string message;
        MessageBoxImage icon;

        if (result.WasCancelled)
        {
            message = $"Batch operation for '{collectionName}' was cancelled.\n\n" +
                     $"Completed: {result.CompletedRequests}/{result.TotalRequests}\n" +
                     $"Time: {result.TotalTime:mm\\:ss}";
            icon = MessageBoxImage.Warning;
        }
        else if (result.FailedRequests > 0)
        {
            message = $"Batch operation for '{collectionName}' completed with errors.\n\n" +
                     $"Total: {result.TotalRequests}\n" +
                     $"Successful: {result.SuccessfulRequests}\n" +
                     $"Failed: {result.FailedRequests}\n" +
                     $"Time: {result.TotalTime:mm\\:ss\\.ff}";
            icon = MessageBoxImage.Warning;
        }
        else
        {
            message = $"All requests in '{collectionName}' completed successfully!\n\n" +
                     $"Requests: {result.SuccessfulRequests}\n" +
                     $"Time: {result.TotalTime:mm\\:ss\\.ff}";
            icon = MessageBoxImage.Information;
        }

        MessageBox.Show(message, "Batch Request Complete", MessageBoxButton.OK, icon);
    }

    private async void ExecuteRenameCollectionMenu(TabCollection? collection)
    {
        if (collection == null) return;

        try
        {
            var dialog = new RenameTabDialog(collection.Name)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                collection.Name = dialog.TabName;
                await _persistenceManager.SaveCollectionAsync(collection);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming collection: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteExportCollectionMenu(TabCollection? collection)
    {
        if (collection == null) return;

        // TODO: Implement collection export functionality
        MessageBox.Show($"Export functionality for collection '{collection.Name}' will be implemented in a future version.", 
            "Feature Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void ExecuteDeleteCollectionMenu(TabCollection? collection)
    {
        if (collection == null) return;
        await ExecuteDeleteCollectionMenuAsync(collection);
    }

    private async Task ExecuteDeleteCollectionMenuAsync(TabCollection collection)
    {
        var result = MessageBox.Show($"Are you sure you want to delete collection '{collection.Name}' and all its tabs?", 
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            // Delete from database
            await _persistenceManager.DeleteCollectionAsync(collection);
            
            // Remove from UI
            TabCollections.Remove(collection);
            if (SelectedCollection == collection)
            {
                SelectedCollection = TabCollections.FirstOrDefault();
                SelectedTab = null;
            }
        }
    }

    private async void AddTabToCollection(RequestType requestType, string defaultName)
    {
        try
        {
            if (SelectedCollection == null) 
            {
                // Create a default collection if none exists
                var defaultCollection = new TabCollection { Name = "Default Collection" };
                TabCollections.Add(defaultCollection);
                SelectedCollection = defaultCollection;
                await _persistenceManager.SaveCollectionAsync(defaultCollection);
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
            
            // Save to database
            await _persistenceManager.SaveTabAsync(tab, SelectedCollection.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error adding tab: {ex}");
            MessageBox.Show($"Error creating new tab: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExecuteNewHttpTab()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => 
            AddTabToCollection(RequestType.HTTP, "New HTTP Request"));
    }

    private async void ExecuteNewWebSocketTab()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => 
            AddTabToCollection(RequestType.WebSocket, "New WebSocket"));
    }

    private async void ExecuteNewGrpcTab()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => 
            AddTabToCollection(RequestType.gRPC, "New gRPC Request"));
    }

    private async void ExecuteRenameTabMenu(RequestTab? tab)
    {
        if (tab == null) return;

        try
        {
            var dialog = new RenameTabDialog(tab.Name)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                tab.Name = dialog.TabName;
                
                // Find the collection that contains this tab
                var parentCollection = TabCollections.FirstOrDefault(c => c.Tabs.Contains(tab));
                if (parentCollection != null)
                {
                    await _persistenceManager.SaveTabAsync(tab, parentCollection.Id);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error renaming tab: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExecuteMoveTabMenu(RequestTab? tabToMove)
    {
        if (tabToMove == null) return;

        // Find the current collection containing this tab
        var currentCollection = TabCollections.FirstOrDefault(c => c.Tabs.Contains(tabToMove));
        if (currentCollection == null) return;

        // Check if there are other collections to move to
        if (TabCollections.Count <= 1)
        {
            MessageBox.Show("There are no other collections to move this tab to. Create a new collection first.", 
                "No Target Collections", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new MoveTabDialog(tabToMove, TabCollections, currentCollection)
            {
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                TabCollection targetCollection;

                if (dialog.CreateNewCollection)
                {
                    // Create new collection
                    targetCollection = new TabCollection { Name = dialog.NewCollectionName };
                    TabCollections.Add(targetCollection);
                    await _persistenceManager.SaveCollectionAsync(targetCollection);
                }
                else if (dialog.SelectedTargetCollection != null)
                {
                    targetCollection = dialog.SelectedTargetCollection;
                }
                else
                {
                    return; // Should not happen, but safety check
                }

                // Move the tab
                currentCollection.Tabs.Remove(tabToMove);
                targetCollection.Tabs.Add(tabToMove);

                // Update in database
                await _persistenceManager.MoveTabAsync(tabToMove, targetCollection);

                // Update selections
                SelectedCollection = targetCollection;
                SelectedTab = tabToMove;

                // Show success message
                MessageBox.Show($"Tab '{tabToMove.Name}' moved to collection '{targetCollection.Name}' successfully.", 
                    "Tab Moved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error moving tab: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExecuteCloseTabMenu(RequestTab? tabToClose)
    {
        if (tabToClose == null) return;

        // Find the collection containing this tab
        var collection = TabCollections.FirstOrDefault(c => c.Tabs.Contains(tabToClose));
        if (collection != null)
        {
            collection.Tabs.Remove(tabToClose);
            if (SelectedTab == tabToClose)
            {
                SelectedTab = collection.Tabs.FirstOrDefault();
            }
            
            // Delete from database
            await _persistenceManager.DeleteTabAsync(tabToClose);
        }
    }

    private async void ExecuteCloseTab(RequestTab? tab)
    {
        if (tab == null || SelectedCollection == null) return;

        SelectedCollection.Tabs.Remove(tab);
        if (SelectedTab == tab)
        {
            SelectedTab = SelectedCollection.Tabs.FirstOrDefault();
        }
        
        // Delete from database
        await _persistenceManager.DeleteTabAsync(tab);
    }

    private void ExecuteImportOpenApi()
    {
        MessageBox.Show("Import functionality will be implemented in a future version.", 
            "Feature Coming Soon", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private string GenerateTabNameFromRequest(HttpRequest httpRequest)
    {
        try
        {
            var method = httpRequest.Method.ToUpper();
            var uri = new Uri(httpRequest.Url);
            var pathSegments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (pathSegments.Length > 0)
            {
                var lastSegment = pathSegments.Last();
                // Clean up path parameters (remove {id}, {param}, etc.)
                lastSegment = System.Text.RegularExpressions.Regex.Replace(lastSegment, @"\{[^}]+\}", "");
                
                if (!string.IsNullOrEmpty(lastSegment))
                {
                    return $"{method} {lastSegment}";
                }
            }
            
            return $"{method} {uri.AbsolutePath}";
        }
        catch
        {
            return $"{httpRequest.Method} Request";
        }
    }

    public void HandleTreeViewSelectionChanged(object selectedItem)
    {
        if (selectedItem is TabCollection collection)
        {
            SelectedCollection = collection;
            // Don't change selected tab when selecting collection
        }
        else if (selectedItem is RequestTab tab)
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

    public async Task ShutdownAsync()
    {
        try
        {
            // Disable auto-save FIRST to prevent race conditions
            _persistenceManager.DisableAutoSave();
            
            // Wait a moment to ensure any pending auto-save operations complete
            await Task.Delay(100);
            
            // Perform final save
            await SaveAllDataAsync();
            
            // Dispose services
            _persistenceManager.Dispose();
            _databaseService.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during shutdown: {ex}");
        }
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}