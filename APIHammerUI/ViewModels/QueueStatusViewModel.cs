using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using APIHammerUI.Services;

namespace APIHammerUI.ViewModels;

/// <summary>
/// ViewModel for monitoring queue status and application performance
/// </summary>
public class QueueStatusViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DispatcherTimer _updateTimer;
    private int _httpRequestQueueSize;
    private int _uiUpdateQueueSize;
    private bool _disposed;

    public QueueStatusViewModel()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500) // Update every 500ms
        };
        _updateTimer.Tick += UpdateQueueStats;
        _updateTimer.Start();
    }

    public int HttpRequestQueueSize
    {
        get => _httpRequestQueueSize;
        private set
        {
            if (_httpRequestQueueSize != value)
            {
                _httpRequestQueueSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalQueueSize));
                OnPropertyChanged(nameof(IsProcessing));
            }
        }
    }

    public int UiUpdateQueueSize
    {
        get => _uiUpdateQueueSize;
        private set
        {
            if (_uiUpdateQueueSize != value)
            {
                _uiUpdateQueueSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalQueueSize));
                OnPropertyChanged(nameof(IsProcessing));
            }
        }
    }

    public int TotalQueueSize => HttpRequestQueueSize + UiUpdateQueueSize;

    public bool IsProcessing => TotalQueueSize > 0;

    public string StatusText => IsProcessing 
        ? $"Processing: {HttpRequestQueueSize} requests, {UiUpdateQueueSize} UI updates"
        : "Idle";

    private void UpdateQueueStats(object? sender, EventArgs e)
    {
        try
        {
            var stats = ApplicationServiceManager.Instance.MessageQueue.GetQueueStats();
            HttpRequestQueueSize = stats.HttpRequestQueueSize;
            UiUpdateQueueSize = stats.UiUpdateQueueSize;
            OnPropertyChanged(nameof(StatusText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating queue stats: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _updateTimer.Stop();
        _updateTimer.Tick -= UpdateQueueStats;
    }
}