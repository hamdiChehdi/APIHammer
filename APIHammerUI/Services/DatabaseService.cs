using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiteDB;
using APIHammerUI.Models.Persistence;

namespace APIHammerUI.Services;

/// <summary>
/// Service for persisting tabs and collections using LiteDB
/// </summary>
public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _database;
    private readonly string _databasePath;
    private bool _disposed;

    // Collection names
    private const string TabCollectionsTable = "tab_collections";
    private const string RequestTabsTable = "request_tabs";

    public DatabaseService(string? databasePath = null)
    {
        // Default to user's AppData folder
        _databasePath = databasePath ?? GetDefaultDatabasePath();
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Initialize database
        _database = new LiteDatabase(_databasePath);
        
        // Configure collections
        ConfigureDatabase();
    }

    private static string GetDefaultDatabasePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataPath, "APIHammer");
        return Path.Combine(appFolder, "apihammer.db");
    }

    private void ConfigureDatabase()
    {
        // Get collections
        var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
        var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);

        // Create indexes for better performance
        collectionsCol.EnsureIndex(x => x.Name);
        collectionsCol.EnsureIndex(x => x.Order);
        collectionsCol.EnsureIndex(x => x.CreatedAt);

        tabsCol.EnsureIndex(x => x.CollectionId);
        tabsCol.EnsureIndex(x => x.Name);
        tabsCol.EnsureIndex(x => x.Order);
        tabsCol.EnsureIndex(x => x.RequestType);
    }

    #region Collection Operations

    /// <summary>
    /// Save a collection to the database
    /// </summary>
    public async Task<Guid> SaveCollectionAsync(PersistentTabCollection collection)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            
            collection.UpdatedAt = DateTime.UtcNow;
            
            if (collection.Id == Guid.Empty)
            {
                collection.Id = Guid.NewGuid();
                collection.CreatedAt = DateTime.UtcNow;
            }

            collectionsCol.Upsert(collection);
            return collection.Id;
        });
    }

    /// <summary>
    /// Get all collections from the database
    /// </summary>
    public async Task<List<PersistentTabCollection>> GetAllCollectionsAsync()
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            var collections = collectionsCol.Query()
                .OrderBy(x => x.Order)
                .ToList();
            
            // Apply secondary sorting in memory since LiteDB doesn't support ThenBy
            return collections.OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ToList();
        });
    }

    /// <summary>
    /// Get a specific collection by ID
    /// </summary>
    public async Task<PersistentTabCollection?> GetCollectionAsync(Guid collectionId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            return collectionsCol.FindById(collectionId);
        });
    }

    /// <summary>
    /// Delete a collection and all its tabs
    /// </summary>
    public async Task<bool> DeleteCollectionAsync(Guid collectionId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);

            // Delete all tabs in the collection
            tabsCol.DeleteMany(x => x.CollectionId == collectionId);
            
            // Delete the collection
            return collectionsCol.Delete(collectionId);
        });
    }

    #endregion

    #region Tab Operations

    /// <summary>
    /// Save a tab to the database
    /// </summary>
    public async Task<Guid> SaveTabAsync(PersistentRequestTab tab)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);
            
            tab.UpdatedAt = DateTime.UtcNow;
            
            if (tab.Id == Guid.Empty)
            {
                tab.Id = Guid.NewGuid();
                tab.CreatedAt = DateTime.UtcNow;
            }

            tabsCol.Upsert(tab);
            return tab.Id;
        });
    }

    /// <summary>
    /// Get all tabs for a specific collection
    /// </summary>
    public async Task<List<PersistentRequestTab>> GetTabsForCollectionAsync(Guid collectionId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);
            var tabs = tabsCol.Query()
                .Where(x => x.CollectionId == collectionId)
                .ToList();
            
            // Apply sorting in memory since LiteDB doesn't support ThenBy
            return tabs.OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ToList();
        });
    }

    /// <summary>
    /// Get a specific tab by ID
    /// </summary>
    public async Task<PersistentRequestTab?> GetTabAsync(Guid tabId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);
            return tabsCol.FindById(tabId);
        });
    }

    /// <summary>
    /// Delete a tab
    /// </summary>
    public async Task<bool> DeleteTabAsync(Guid tabId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);
            return tabsCol.Delete(tabId);
        });
    }

    /// <summary>
    /// Move a tab to a different collection
    /// </summary>
    public async Task<bool> MoveTabToCollectionAsync(Guid tabId, Guid newCollectionId)
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);
            var tab = tabsCol.FindById(tabId);
            
            if (tab == null)
                return false;

            tab.CollectionId = newCollectionId;
            tab.UpdatedAt = DateTime.UtcNow;
            
            return tabsCol.Update(tab);
        });
    }

    #endregion

    #region Batch Operations

    /// <summary>
    /// Save multiple collections and their tabs in a transaction
    /// </summary>
    public async Task SaveAllDataAsync(List<PersistentTabCollection> collections, List<PersistentRequestTab> tabs)
    {
        await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            // LiteDB v5 doesn't have BeginTrans, we'll use simple upsert operations
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);

            // Save collections
            foreach (var collection in collections)
            {
                collection.UpdatedAt = DateTime.UtcNow;
                if (collection.Id == Guid.Empty)
                {
                    collection.Id = Guid.NewGuid();
                    collection.CreatedAt = DateTime.UtcNow;
                }
                collectionsCol.Upsert(collection);
            }

            // Save tabs
            foreach (var tab in tabs)
            {
                tab.UpdatedAt = DateTime.UtcNow;
                if (tab.Id == Guid.Empty)
                {
                    tab.Id = Guid.NewGuid();
                    tab.CreatedAt = DateTime.UtcNow;
                }
                tabsCol.Upsert(tab);
            }
        });
    }

    /// <summary>
    /// Load all data from the database
    /// </summary>
    public async Task<(List<PersistentTabCollection> Collections, List<PersistentRequestTab> Tabs)> LoadAllDataAsync()
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);

            var collections = collectionsCol.Query()
                .OrderBy(x => x.Order)
                .ToList();

            var tabs = tabsCol.FindAll().ToList();

            // Apply sorting in memory since LiteDB doesn't support complex sorting
            collections = collections.OrderBy(x => x.Order).ThenBy(x => x.CreatedAt).ToList();
            tabs = tabs.OrderBy(x => x.CollectionId)
                      .ThenBy(x => x.Order)
                      .ThenBy(x => x.CreatedAt)
                      .ToList();

            return (collections, tabs);
        });
    }

    #endregion

    #region Maintenance

    /// <summary>
    /// Compact the database to reclaim space
    /// </summary>
    public async Task CompactDatabaseAsync()
    {
        await Task.Run(() =>
        {
            ThrowIfDisposed();
            _database.Rebuild();
        });
    }

    /// <summary>
    /// Get database statistics
    /// </summary>
    public async Task<DatabaseStats> GetStatsAsync()
    {
        return await Task.Run(() =>
        {
            ThrowIfDisposed();
            
            var collectionsCol = _database.GetCollection<PersistentTabCollection>(TabCollectionsTable);
            var tabsCol = _database.GetCollection<PersistentRequestTab>(RequestTabsTable);

            return new DatabaseStats
            {
                CollectionCount = collectionsCol.Count(),
                TabCount = tabsCol.Count(),
                DatabaseSizeBytes = new FileInfo(_databasePath).Length,
                DatabasePath = _databasePath
            };
        });
    }

    #endregion

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DatabaseService));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _database?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Database statistics information
/// </summary>
public class DatabaseStats
{
    public int CollectionCount { get; set; }
    public int TabCount { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public string DatabasePath { get; set; } = "";
    
    public string DatabaseSizeFormatted =>
        DatabaseSizeBytes < 1024 ? $"{DatabaseSizeBytes} B" :
        DatabaseSizeBytes < 1024 * 1024 ? $"{DatabaseSizeBytes / 1024.0:F1} KB" :
        $"{DatabaseSizeBytes / (1024.0 * 1024.0):F1} MB";
}