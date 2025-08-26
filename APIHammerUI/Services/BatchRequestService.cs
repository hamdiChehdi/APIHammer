using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using APIHammerUI.Models;
using APIHammerUI.Views;

namespace APIHammerUI.Services;

public class BatchRequestResult
{
    public int TotalRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public TimeSpan TotalTime { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool WasCancelled { get; set; }
}

public class BatchRequestProgress
{
    public int CurrentRequest { get; set; }
    public int TotalRequests { get; set; }
    public string CurrentRequestName { get; set; } = "";
    public string Status { get; set; } = "";
}

public class BatchRequestService
{
    private readonly HttpRequestService _httpRequestService;
    
    public event EventHandler<BatchRequestProgress>? ProgressChanged;

    public BatchRequestService()
    {
        _httpRequestService = new HttpRequestService();
    }
    
    /// <summary>
    /// Sends all HTTP requests in a collection concurrently
    /// </summary>
    public async Task<BatchRequestResult> SendAllRequestsAsync(
        TabCollection collection, 
        CancellationToken cancellationToken = default,
        int maxConcurrency = 5)
    {
        if (collection?.Tabs == null || !collection.Tabs.Any())
        {
            return new BatchRequestResult
            {
                TotalRequests = 0,
                CompletedRequests = 0
            };
        }

        // Get all HTTP request tabs
        var httpTabs = collection.Tabs
            .Where(tab => tab.RequestType == RequestType.HTTP && tab.Content is HttpRequestView)
            .ToList();

        if (!httpTabs.Any())
        {
            return new BatchRequestResult
            {
                TotalRequests = 0,
                CompletedRequests = 0
            };
        }

        var result = new BatchRequestResult
        {
            TotalRequests = httpTabs.Count
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Create semaphore to limit concurrency
            using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            
            // Create tasks for all requests
            var tasks = httpTabs.Select(async (tab, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                
                try
                {
                    return await SendSingleRequestAsync(tab, index + 1, result.TotalRequests, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            // Wait for all requests to complete
            var requestResults = await Task.WhenAll(tasks);
            
            // Aggregate results
            result.CompletedRequests = requestResults.Length;
            result.SuccessfulRequests = requestResults.Count(r => r.Success);
            result.FailedRequests = requestResults.Count(r => !r.Success);
            result.Errors = requestResults.Where(r => !r.Success).Select(r => r.ErrorMessage).ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result.WasCancelled = true;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Batch operation failed: {ex.Message}");
        }
        finally
        {
            stopwatch.Stop();
            result.TotalTime = stopwatch.Elapsed;
        }

        return result;
    }

    private async Task<SingleRequestResult> SendSingleRequestAsync(
        RequestTab tab, 
        int requestIndex, 
        int totalRequests, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Report progress
            ProgressChanged?.Invoke(this, new BatchRequestProgress
            {
                CurrentRequest = requestIndex,
                TotalRequests = totalRequests,
                CurrentRequestName = tab.Name,
                Status = "Sending..."
            });

            if (tab.Content is not HttpRequestView httpView)
            {
                return new SingleRequestResult
                {
                    Success = false,
                    ErrorMessage = $"Tab '{tab.Name}' is not an HTTP request"
                };
            }

            var httpRequest = httpView.DataContext as HttpRequest;
            if (httpRequest == null)
            {
                return new SingleRequestResult
                {
                    Success = false,
                    ErrorMessage = $"Tab '{tab.Name}' has no HTTP request data"
                };
            }

            // Validate the request
            if (string.IsNullOrWhiteSpace(httpRequest.Url))
            {
                return new SingleRequestResult
                {
                    Success = false,
                    ErrorMessage = $"Tab '{tab.Name}' has no URL specified"
                };
            }

            // Update UI to show loading state
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                httpRequest.IsLoading = true;
                httpRequest.Response = "Sending request...";
            });

            // Send the request using the HttpRequestService
            var requestResult = await _httpRequestService.SendRequestAsync(httpRequest, cancellationToken);

            // Update the UI with the results
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                httpRequest.IsLoading = false;
                httpRequest.Response = requestResult.Response;
                httpRequest.ResponseTime = requestResult.ResponseTime;
                httpRequest.ResponseSize = requestResult.ResponseSize;
                httpRequest.RequestDateTime = requestResult.RequestDateTime;
            });

            return new SingleRequestResult
            {
                Success = requestResult.Success,
                ErrorMessage = requestResult.Success ? null : $"Request '{tab.Name}' failed: {requestResult.ErrorMessage}"
            };
        }
        catch (Exception ex)
        {
            // Ensure UI is updated even on error
            if (tab.Content is HttpRequestView httpView && httpView.DataContext is HttpRequest httpRequest)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.IsLoading = false;
                    httpRequest.Response = $"Error: {ex.Message}";
                });
            }

            return new SingleRequestResult
            {
                Success = false,
                ErrorMessage = $"Request '{tab.Name}' failed: {ex.Message}"
            };
        }
    }

    private class SingleRequestResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}