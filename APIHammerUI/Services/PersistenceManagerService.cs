using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using APIHammerUI.Models;
using APIHammerUI.Models.Persistence;

namespace APIHammerUI.Services;

/// <summary>
/// Service that manages automatic persistence of collections and tabs
/// </summary>
public class PersistenceManagerService : IDisposable
{
    private readonly DatabaseService _databaseService;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _disposed;
    private bool _isLoading;

    // Auto-save interval
    private const int AutoSaveIntervalMinutes = 2;

    public PersistenceManagerService(DatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        
        // Setup auto-save timer
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(AutoSaveIntervalMinutes)
        };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
    }

    /// <summary>
    /// Enable automatic saving
    /// </summary>
    public void EnableAutoSave()
    {
        _autoSaveTimer.Start();
    }

    /// <summary>
    /// Disable automatic saving
    /// </summary>
    public void DisableAutoSave()
    {
        _autoSaveTimer.Stop();
    }

    /// <summary>
    /// Load all collections and tabs from the database
    /// </summary>
    public async Task<ObservableCollection<TabCollection>> LoadCollectionsAsync()
    {
        try
        {
            _isLoading = true;
            
            var (persistentCollections, persistentTabs) = await _databaseService.LoadAllDataAsync();
            var collections = new ObservableCollection<TabCollection>();

            // If no data exists, create a default collection
            if (!persistentCollections.Any())
            {
                var defaultCollection = new TabCollection { Name = "Default Collection" };
                collections.Add(defaultCollection);
                return collections;
            }

            // Convert persistent collections to UI collections
            foreach (var persistentCollection in persistentCollections)
            {
                var collection = ModelMappingService.FromPersistent(persistentCollection);
                
                // Add tabs to collection
                var collectionTabs = persistentTabs
                    .Where(t => t.CollectionId == persistentCollection.Id)
                    .OrderBy(t => t.Order)
                    .ThenBy(t => t.CreatedAt)
                    .ToList();

                foreach (var persistentTab in collectionTabs)
                {
                    var tab = ModelMappingService.FromPersistent(persistentTab);
                    collection.Tabs.Add(tab);
                }

                collections.Add(collection);
            }

            return collections;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// Save all collections and tabs to the database
    /// </summary>
    public async Task SaveCollectionsAsync(ObservableCollection<TabCollection> collections)
    {
        if (_isLoading || _disposed) return; // Don't save while loading or after disposal

        try
        {
            var persistentCollections = new List<PersistentTabCollection>();
            var persistentTabs = new List<PersistentRequestTab>();

            // Convert UI collections to persistent collections
            for (int i = 0; i < collections.Count; i++)
            {
                var collection = collections[i];
                var persistentCollection = ModelMappingService.ToPersistent(collection);
                persistentCollection.Order = i;
                persistentCollections.Add(persistentCollection);

                // Convert tabs
                for (int j = 0; j < collection.Tabs.Count; j++)
                {
                    var tab = collection.Tabs[j];
                    var persistentTab = ModelMappingService.ToPersistent(tab, collection.Id);
                    persistentTab.Order = j;
                    persistentTabs.Add(persistentTab);
                }
            }

            // Save to database
            await _databaseService.SaveAllDataAsync(persistentCollections, persistentTabs);
        }
        catch (ObjectDisposedException)
        {
            // Database service was disposed - this is expected during shutdown
            System.Diagnostics.Debug.WriteLine("SaveCollectionsAsync: Database service was disposed during shutdown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving collections: {ex}");
            // Don't throw here to avoid disrupting the UI
        }
    }

    /// <summary>
    /// Save a single collection
    /// </summary>
    public async Task SaveCollectionAsync(TabCollection collection)
    {
        if (_isLoading || _disposed) return;

        try
        {
            var persistentCollection = ModelMappingService.ToPersistent(collection);
            
            await _databaseService.SaveCollectionAsync(persistentCollection);

            // Save all tabs in the collection
            for (int i = 0; i < collection.Tabs.Count; i++)
            {
                var tab = collection.Tabs[i];
                var persistentTab = ModelMappingService.ToPersistent(tab, collection.Id);
                persistentTab.Order = i;
                await _databaseService.SaveTabAsync(persistentTab);
            }
        }
        catch (ObjectDisposedException)
        {
            // Database service was disposed - this is expected during shutdown
            System.Diagnostics.Debug.WriteLine("SaveCollectionAsync: Database service was disposed during shutdown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving collection: {ex}");
        }
    }

    /// <summary>
    /// Save a single tab
    /// </summary>
    public async Task SaveTabAsync(RequestTab tab, Guid collectionId)
    {
        if (_isLoading || _disposed) return;

        try
        {
            var persistentTab = ModelMappingService.ToPersistent(tab, collectionId);
            await _databaseService.SaveTabAsync(persistentTab);
        }
        catch (ObjectDisposedException)
        {
            // Database service was disposed - this is expected during shutdown
            System.Diagnostics.Debug.WriteLine("SaveTabAsync: Database service was disposed during shutdown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving tab: {ex}");
        }
    }

    /// <summary>
    /// Delete a collection from the database
    /// </summary>
    public async Task DeleteCollectionAsync(TabCollection collection)
    {
        try
        {
            await _databaseService.DeleteCollectionAsync(collection.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting collection: {ex}");
        }
    }

    /// <summary>
    /// Delete a tab from the database
    /// </summary>
    public async Task DeleteTabAsync(RequestTab tab)
    {
        try
        {
            await _databaseService.DeleteTabAsync(tab.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting tab: {ex}");
        }
    }

    /// <summary>
    /// Move a tab to a different collection
    /// </summary>
    public async Task MoveTabAsync(RequestTab tab, TabCollection targetCollection)
    {
        try
        {
            await _databaseService.MoveTabToCollectionAsync(tab.Id, targetCollection.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error moving tab: {ex}");
        }
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<DatabaseStats> GetStatsAsync()
    {
        return await _databaseService.GetStatsAsync();
    }

    /// <summary>
    /// Compact the database
    /// </summary>
    public async Task CompactDatabaseAsync()
    {
        await _databaseService.CompactDatabaseAsync();
    }

    private async void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        // Check if we're disposed before triggering auto-save
        if (_disposed) return;
        
        // The actual saving will be triggered by the ViewModel
        AutoSaveRequested?.Invoke();
    }

    /// <summary>
    /// Event raised when auto-save should be performed
    /// </summary>
    public event Action? AutoSaveRequested;

    public void Dispose()
    {
        if (_disposed) return;

        // Stop the timer first to prevent new auto-save requests
        _autoSaveTimer?.Stop();
        
        // Unsubscribe from timer events
        if (_autoSaveTimer != null)
        {
            _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
        }
        
        _disposed = true;
    }
}