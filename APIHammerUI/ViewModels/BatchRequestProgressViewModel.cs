using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using APIHammerUI.Commands;
using APIHammerUI.Helpers;
using APIHammerUI.Models;
using APIHammerUI.Services;

namespace APIHammerUI.ViewModels;

public class BatchRequestProgressViewModel : ObservableObject
{
    private readonly TabCollection _collection;
    private readonly BatchRequestService _batchService = new();
    private CancellationTokenSource? _cts;
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _startTime;

    private string _collectionName = string.Empty;
    private string _overallProgressText = "Preparing requests...";
    private double _overallProgress;
    private string _currentRequestText = "Initializing...";
    private int _totalRequests;
    private int _completedRequests;
    private int _failedRequests;
    private string _elapsedTimeText = "Elapsed: 00:00";
    private string _statusText = "Initializing batch request operation...";
    private string _titleText = "Sending Collection Requests";
    private bool _isCloseEnabled;
    private bool _isCancelEnabled = true;
    private bool _operationCompleted;

    public event EventHandler? RequestClose; // Raised when dialog should close (after completion)

    public BatchRequestResult? Result { get; private set; }

    public string CollectionName
    {
        get => _collectionName;
        set => SetProperty(ref _collectionName, value);
    }

    public string OverallProgressText
    {
        get => _overallProgressText;
        set => SetProperty(ref _overallProgressText, value);
    }

    public double OverallProgress
    {
        get => _overallProgress;
        set => SetProperty(ref _overallProgress, value);
    }

    public string CurrentRequestText
    {
        get => _currentRequestText;
        set => SetProperty(ref _currentRequestText, value);
    }

    public int TotalRequests
    {
        get => _totalRequests;
        set => SetProperty(ref _totalRequests, value);
    }

    public int CompletedRequests
    {
        get => _completedRequests;
        set => SetProperty(ref _completedRequests, value);
    }

    public int FailedRequests
    {
        get => _failedRequests;
        set => SetProperty(ref _failedRequests, value);
    }

    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetProperty(ref _elapsedTimeText, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string TitleText
    {
        get => _titleText;
        set => SetProperty(ref _titleText, value);
    }

    public bool IsCloseEnabled
    {
        get => _isCloseEnabled;
        set { if (SetProperty(ref _isCloseEnabled, value)) (CloseCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public bool IsCancelEnabled
    {
        get => _isCancelEnabled;
        set { if (SetProperty(ref _isCancelEnabled, value)) (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged(); }
    }

    public ICommand CancelCommand { get; }
    public ICommand CloseCommand { get; }

    public BatchRequestProgressViewModel(TabCollection collection)
    {
        _collection = collection;
        CollectionName = collection.Name;

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();

        _batchService.ProgressChanged += BatchService_ProgressChanged;

        CancelCommand = new RelayCommand(ExecuteCancel, () => IsCancelEnabled);
        CloseCommand = new RelayCommand(ExecuteClose, () => IsCloseEnabled);
    }

    private void UpdateElapsed()
    {
        var elapsed = DateTime.Now - _startTime;
        ElapsedTimeText = $"Elapsed: {elapsed:mm\\:ss}";
    }

    public async Task StartAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _startTime = DateTime.Now;
            _elapsedTimer.Start();
            TotalRequests = _collection.Tabs.Count;
            StatusText = "Starting batch request operation...";

            Result = await _batchService.SendAllRequestsAsync(_collection, _cts.Token);
            _operationCompleted = true;
            _elapsedTimer.Stop();
            ApplyCompletionState();
        }
        catch (Exception ex)
        {
            _operationCompleted = true;
            _elapsedTimer.Stop();
            ApplyErrorState(ex);
        }
    }

    private void BatchService_ProgressChanged(object? sender, BatchRequestProgress e)
    {
        // Ensure updates on UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            OverallProgressText = $"Request {e.CurrentRequest} of {e.TotalRequests}";
            OverallProgress = (double)e.CurrentRequest / e.TotalRequests * 100.0;
            CurrentRequestText = $"{e.CurrentRequestName} - {e.Status}";
            CompletedRequests = e.CurrentRequest - 1; // Previous completed
        });
    }

    private void ApplyCompletionState()
    {
        if (Result == null) return;
        OverallProgress = 100;
        CompletedRequests = Result.CompletedRequests;
        FailedRequests = Result.FailedRequests;

        if (Result.WasCancelled)
        {
            StatusText = "Batch operation was cancelled.";
            TitleText = "Batch Operation Cancelled";
        }
        else if (Result.FailedRequests > 0)
        {
            StatusText = $"Batch operation completed with {Result.FailedRequests} failures.";
            TitleText = "Batch Operation Completed with Errors";
        }
        else
        {
            StatusText = $"All {Result.SuccessfulRequests} requests completed successfully!";
            TitleText = "Batch Operation Completed";
        }

        CurrentRequestText = $"Total time: {Result.TotalTime:mm\\:ss\\.ff}";
        IsCancelEnabled = false;
        IsCloseEnabled = true;
    }

    private void ApplyErrorState(Exception ex)
    {
        StatusText = $"Batch operation failed: {ex.Message}";
        TitleText = "Batch Operation Failed";
        CurrentRequestText = "Operation terminated due to error.";
        IsCancelEnabled = false;
        IsCloseEnabled = true;
    }

    private void ExecuteCancel()
    {
        try
        {
            if (_cts == null || _cts.IsCancellationRequested) return;
            _cts.Cancel();
            _elapsedTimer.Stop();
            StatusText = "Cancelling batch operation...";
            IsCancelEnabled = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Error cancelling: {ex.Message}";
        }
    }

    private void ExecuteClose()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
