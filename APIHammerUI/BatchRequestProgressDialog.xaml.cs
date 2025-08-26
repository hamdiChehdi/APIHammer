using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using APIHammerUI.Models;
using APIHammerUI.Services;

namespace APIHammerUI;

public partial class BatchRequestProgressDialog : Window, INotifyPropertyChanged
{
    private readonly BatchRequestService _batchService;
    private readonly TabCollection _collection;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly DispatcherTimer _elapsedTimer;
    private DateTime _startTime;
    private BatchRequestResult? _result;

    private string _collectionName = "";
    public string CollectionName
    {
        get => _collectionName;
        set
        {
            _collectionName = value;
            OnPropertyChanged(nameof(CollectionName));
        }
    }

    public BatchRequestResult? Result => _result;

    public BatchRequestProgressDialog(TabCollection collection)
    {
        InitializeComponent();
        DataContext = this;

        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _batchService = new BatchRequestService();
        CollectionName = collection.Name;

        // Setup timer for elapsed time display
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += ElapsedTimer_Tick;

        // Subscribe to progress updates
        _batchService.ProgressChanged += BatchService_ProgressChanged;

        // Start the batch operation when the dialog loads
        Loaded += BatchRequestProgressDialog_Loaded;
    }

    private async void BatchRequestProgressDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await StartBatchOperation();
    }

    private async Task StartBatchOperation()
    {
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _startTime = DateTime.Now;
            _elapsedTimer.Start();

            // Update UI
            StatusTextBlock.Text = "Starting batch request operation...";
            TotalRequestsTextBlock.Text = _collection.Tabs.Count.ToString();

            // Start the batch operation
            _result = await _batchService.SendAllRequestsAsync(_collection, _cancellationTokenSource.Token);

            // Operation completed
            _elapsedTimer.Stop();
            OnBatchCompleted(_result);
        }
        catch (Exception ex)
        {
            _elapsedTimer.Stop();
            OnBatchError(ex);
        }
    }

    private void BatchService_ProgressChanged(object? sender, BatchRequestProgress e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            OverallProgressTextBlock.Text = $"Request {e.CurrentRequest} of {e.TotalRequests}";
            OverallProgressBar.Value = (double)e.CurrentRequest / e.TotalRequests * 100;
            CurrentRequestTextBlock.Text = $"{e.CurrentRequestName} - {e.Status}";
            CompletedRequestsTextBlock.Text = (e.CurrentRequest - 1).ToString();
        });
    }

    private void ElapsedTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = DateTime.Now - _startTime;
        ElapsedTimeTextBlock.Text = $"Elapsed: {elapsed:mm\\:ss}";
    }

    private void OnBatchCompleted(BatchRequestResult result)
    {
        Dispatcher.InvokeAsync(() =>
        {
            OverallProgressBar.Value = 100;
            CompletedRequestsTextBlock.Text = result.CompletedRequests.ToString();
            FailedRequestsTextBlock.Text = result.FailedRequests.ToString();

            if (result.WasCancelled)
            {
                StatusTextBlock.Text = "Batch operation was cancelled.";
                TitleTextBlock.Text = "Batch Operation Cancelled";
            }
            else if (result.FailedRequests > 0)
            {
                StatusTextBlock.Text = $"Batch operation completed with {result.FailedRequests} failures.";
                TitleTextBlock.Text = "Batch Operation Completed with Errors";
            }
            else
            {
                StatusTextBlock.Text = $"All {result.SuccessfulRequests} requests completed successfully!";
                TitleTextBlock.Text = "Batch Operation Completed";
            }

            CurrentRequestTextBlock.Text = $"Total time: {result.TotalTime:mm\\:ss\\.ff}";

            // Enable close button and disable cancel
            CloseButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        });
    }

    private void OnBatchError(Exception ex)
    {
        Dispatcher.InvokeAsync(() =>
        {
            StatusTextBlock.Text = $"Batch operation failed: {ex.Message}";
            TitleTextBlock.Text = "Batch Operation Failed";
            CurrentRequestTextBlock.Text = "Operation terminated due to error.";

            // Enable close button and disable cancel
            CloseButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        });
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _elapsedTimer.Stop();
            
            StatusTextBlock.Text = "Cancelling batch operation...";
            CancelButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error cancelling operation: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // If operation is still running, ask for confirmation
        if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            var result = MessageBox.Show(
                "Batch operation is still running. Do you want to cancel it and close?",
                "Confirm Close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            _cancellationTokenSource.Cancel();
        }

        // Cleanup
        _elapsedTimer?.Stop();
        _cancellationTokenSource?.Dispose();
        
        base.OnClosing(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}