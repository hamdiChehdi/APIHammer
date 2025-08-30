using System;
using System.Windows.Threading;
using APIHammerUI.Services;

namespace APIHammerUI.Services;

/// <summary>
/// Singleton service manager for the application
/// </summary>
public class ApplicationServiceManager : IDisposable
{
    private static ApplicationServiceManager? _instance;
    private static readonly object _lock = new();
    
    private readonly MessageQueueService _messageQueueService;
    private bool _disposed;

    private ApplicationServiceManager(Dispatcher dispatcher)
    {
        _messageQueueService = new MessageQueueService(dispatcher);
    }

    /// <summary>
    /// Initialize the service manager with the application dispatcher
    /// </summary>
    public static void Initialize(Dispatcher dispatcher)
    {
        lock (_lock)
        {
            if (_instance != null)
                throw new InvalidOperationException("ApplicationServiceManager is already initialized");

            _instance = new ApplicationServiceManager(dispatcher);
        }
    }

    /// <summary>
    /// Get the singleton instance
    /// </summary>
    public static ApplicationServiceManager Instance
    {
        get
        {
            lock (_lock)
            {
                if (_instance == null)
                    throw new InvalidOperationException("ApplicationServiceManager is not initialized. Call Initialize() first.");
                
                return _instance;
            }
        }
    }

    /// <summary>
    /// Get the message queue service
    /// </summary>
    public MessageQueueService MessageQueue => _messageQueueService;

    /// <summary>
    /// Shutdown the service manager
    /// </summary>
    public static void Shutdown()
    {
        lock (_lock)
        {
            _instance?.Dispose();
            _instance = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _messageQueueService.Dispose();
    }
}