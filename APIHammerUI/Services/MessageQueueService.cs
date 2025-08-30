using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using APIHammerUI.Messages;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Text.Json;
using APIHammerUI.Models;
using RequestQueueMessage = APIHammerUI.Messages.HttpRequestMessage; // alias to avoid ambiguity

namespace APIHammerUI.Services;

/// <summary>
/// Service that manages message queues and provides producer/consumer pattern implementation
/// </summary>
public class MessageQueueService : IDisposable
{
    private readonly MessageQueue _httpRequestQueue;
    private readonly MessageQueue _uiUpdateQueue;
    private readonly HttpRequestService _httpRequestService; // kept for non?streaming fallback
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    
    private Task? _httpRequestProcessor;
    private Task? _uiUpdateProcessor;
    private bool _disposed;

    // Streaming / limits
    private const int MAX_RESPONSE_SIZE = 10 * 1024 * 1024; // 10 MB
    private const int STREAM_BUFFER_SIZE = 16 * 1024; // 16 KB
    private const int UI_UPDATE_INTERVAL_MS = 300; // throttle UI updates
    private static readonly int MAX_DISPLAY_SIZE = 10 * 1024; // 10 KB limit for displayed response

    private static readonly HttpClient _streamHttpClient = new()
    {
        MaxResponseContentBufferSize = MAX_RESPONSE_SIZE,
        Timeout = TimeSpan.FromMinutes(5)
    };

    static MessageQueueService()
    {
        if (!_streamHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _streamHttpClient.DefaultRequestHeaders.Add("User-Agent", "APIHammer/1.0");
    }

    public MessageQueueService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _httpRequestQueue = new MessageQueue();
        _uiUpdateQueue = new MessageQueue();
        _httpRequestService = new HttpRequestService();
        
        StartProcessors();
    }

    /// <summary>
    /// Queue an HTTP request for processing
    /// </summary>
    public void QueueHttpRequest(RequestQueueMessage requestMessage)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MessageQueueService));

        _httpRequestQueue.Enqueue(requestMessage);
    }

    /// <summary>
    /// Queue a UI update operation
    /// </summary>
    public void QueueUiUpdate(UiUpdateMessage updateMessage)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MessageQueueService));

        _uiUpdateQueue.Enqueue(updateMessage);
    }

    /// <summary>
    /// Queue a notification message
    /// </summary>
    public void QueueNotification(NotificationMessage notificationMessage)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MessageQueueService));

        _uiUpdateQueue.Enqueue(notificationMessage);
    }

    /// <summary>
    /// Get queue statistics
    /// </summary>
    public (int HttpRequestQueueSize, int UiUpdateQueueSize) GetQueueStats()
    {
        return (_httpRequestQueue.Count, _uiUpdateQueue.Count);
    }

    private void StartProcessors()
    {
        _httpRequestProcessor = Task.Run(ProcessHttpRequestsAsync);
        _uiUpdateProcessor = Task.Run(ProcessUiUpdatesAsync);
    }

    private async Task ProcessHttpRequestsAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var message = await _httpRequestQueue.DequeueAsync(_cancellationTokenSource.Token);
                if (message is not RequestQueueMessage requestMessage)
                    continue;

                // Process request (streaming) on background thread (already off UI thread here)
                await ProcessHttpRequestStreamingAsync(requestMessage, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in HTTP request processor: {ex}");
            }
        }
    }

    private async Task ProcessHttpRequestStreamingAsync(RequestQueueMessage queueMessage, CancellationToken serviceToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, queueMessage.CancellationToken);
        var cancellationToken = linkedCts.Token;
        var model = queueMessage.Request;

        // Initialize request state
        InitializeRequestState(model);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Execute the HTTP request with streaming
            var result = await ExecuteHttpRequestAsync(model, cancellationToken);
            
            // Process the streaming response
            await ProcessStreamingResponseAsync(model, result.Response, result.Request, cancellationToken);
            
            // Finalize the request
            await FinalizeRequestAsync(model, result.Response, result.Request, stopwatch.Elapsed, queueMessage);
        }
        catch (OperationCanceledException) when (queueMessage.CancellationToken.IsCancellationRequested)
        {
            HandleRequestCancellation(model);
        }
        catch (Exception ex)
        {
            HandleRequestError(model, ex, stopwatch.Elapsed);
        }
    }

    private void InitializeRequestState(HttpRequest model)
    {
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 95,
            Description = "Initialize streaming state",
            UiAction = () =>
            {
                model.IsLoading = true;
                model.RequestDateTime = DateTime.Now;
                model.ResponseTime = null;
                model.ResponseSize = null;
                model.Response = string.Empty;
                model.ResponseChunks.Clear();
                model.ResponseChunks.Add("(Connecting...)\n");
            }
        });
    }

    private async Task<(HttpResponseMessage Response, System.Net.Http.HttpRequestMessage Request)> ExecuteHttpRequestAsync(
        HttpRequest model, 
        CancellationToken cancellationToken)
    {
        var httpRequest = CreateHttpRequestMessage(model);
        var response = await _streamHttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        
        // Update UI with response headers
        var headerText = BuildResponseHeaders(response, httpRequest, model);
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 90,
            Description = "Add headers chunk",
            UiAction = () =>
            {
                model.ResponseChunks.Clear();
                model.ResponseChunks.Add(headerText);
            }
        });

        return (response, httpRequest);
    }

    private System.Net.Http.HttpRequestMessage CreateHttpRequestMessage(HttpRequest model)
    {
        if (!Uri.TryCreate(model.FullUrl, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Invalid URL format.");

        var httpRequest = new System.Net.Http.HttpRequestMessage
        {
            Method = GetHttpMethod(model.Method),
            RequestUri = uri
        };

        ApplyAuthentication(httpRequest, model.Authentication);
        ApplyHeaders(httpRequest, model);
        ApplyRequestBody(httpRequest, model);

        return httpRequest;
    }

    private static HttpMethod GetHttpMethod(string method) => method switch
    {
        "GET" => HttpMethod.Get,
        "POST" => HttpMethod.Post,
        "PUT" => HttpMethod.Put,
        "DELETE" => HttpMethod.Delete,
        "PATCH" => HttpMethod.Patch,
        "HEAD" => HttpMethod.Head,
        "OPTIONS" => HttpMethod.Options,
        _ => HttpMethod.Get
    };

    private static void ApplyHeaders(System.Net.Http.HttpRequestMessage httpRequest, HttpRequest model)
    {
        foreach (var header in model.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            // Skip authorization headers that are handled by authentication
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                (model.Authentication.Type == AuthenticationType.BasicAuth || 
                 model.Authentication.Type == AuthenticationType.BearerToken))
                continue;

            httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    private static void ApplyRequestBody(System.Net.Http.HttpRequestMessage httpRequest, HttpRequest model)
    {
        if (string.IsNullOrWhiteSpace(model.Body) || 
            model.Method is not ("POST" or "PUT" or "PATCH"))
            return;

        var contentType = model.Headers
            .FirstOrDefault(h => h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            ?.Value ?? "application/json";

        httpRequest.Content = new StringContent(model.Body, Encoding.UTF8, contentType);
    }

    private async Task ProcessStreamingResponseAsync(
        HttpRequest model, 
        HttpResponseMessage response, 
        System.Net.Http.HttpRequestMessage httpRequest,
        CancellationToken cancellationToken)
    {
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, STREAM_BUFFER_SIZE, leaveOpen: true);

        var responseContent = await StreamResponseContentAsync(model, reader, cancellationToken);
        var formattedContent = await FormatResponseIfNeeded(responseContent.Content, response, responseContent.WasTruncated);

        if (formattedContent != responseContent.Content)
        {
            ReplaceResponseBodyWithFormatted(model, formattedContent);
        }
    }

    private async Task<(string Content, bool WasTruncated)> StreamResponseContentAsync(
        HttpRequest model,
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[STREAM_BUFFER_SIZE];
        var contentBuilder = new StringBuilder();
        var lastEmittedLength = 0;
        var lastEmitTime = DateTime.UtcNow;
        var totalBytes = 0L;
        var wasTruncated = false;

        int bytesRead;
        while ((bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            contentBuilder.Append(buffer, 0, bytesRead);
            totalBytes += Encoding.UTF8.GetByteCount(buffer, 0, bytesRead);

            if (totalBytes > MAX_RESPONSE_SIZE)
            {
                wasTruncated = true;
                break;
            }

            var updateResult = await UpdateUIWithStreamingContentIfNeeded(model, contentBuilder, lastEmittedLength, lastEmitTime);
            lastEmittedLength = updateResult.LastEmittedLength;
            lastEmitTime = updateResult.LastEmitTime;
        }

        // Emit any remaining content
        await EmitRemainingContent(model, contentBuilder, lastEmittedLength);

        if (wasTruncated)
        {
            await AddTruncationNotice(model);
        }

        return (contentBuilder.ToString(), wasTruncated);
    }

    private async Task<(int LastEmittedLength, DateTime LastEmitTime)> UpdateUIWithStreamingContentIfNeeded(
        HttpRequest model, 
        StringBuilder contentBuilder, 
        int lastEmittedLength, 
        DateTime lastEmitTime)
    {
        var now = DateTime.UtcNow;
        if ((now - lastEmitTime).TotalMilliseconds < UI_UPDATE_INTERVAL_MS)
            return (lastEmittedLength, lastEmitTime);

        var currentLength = contentBuilder.Length;
        if (currentLength <= lastEmittedLength)
            return (lastEmittedLength, lastEmitTime);

        var slice = contentBuilder.ToString(lastEmittedLength, currentLength - lastEmittedLength);

        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 60,
            Description = "Append streaming slice",
            UiAction = () => AddContentSliceToModel(model, slice)
        });

        return (currentLength, now);
    }

    private static void AddContentSliceToModel(HttpRequest model, string slice)
    {
        model.ResponseChunks.Add(slice);

        // Trim chunks if they exceed display limit
        var totalSize = model.ResponseChunks.Sum(chunk => Encoding.UTF8.GetByteCount(chunk));
        while (totalSize > MAX_DISPLAY_SIZE && model.ResponseChunks.Count > 1)
        {
            totalSize -= Encoding.UTF8.GetByteCount(model.ResponseChunks[0]);
            model.ResponseChunks.RemoveAt(0);
        }
    }

    private async Task EmitRemainingContent(HttpRequest model, StringBuilder contentBuilder, int lastEmittedLength)
    {
        if (contentBuilder.Length <= lastEmittedLength)
            return;

        var slice = contentBuilder.ToString(lastEmittedLength, contentBuilder.Length - lastEmittedLength);
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 60,
            Description = "Append final streaming slice",
            UiAction = () => model.ResponseChunks.Add(slice)
        });
    }

    private async Task AddTruncationNotice(HttpRequest model)
    {
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 55,
            Description = "Add truncated notice",
            UiAction = () => model.ResponseChunks.Add("\n[Response truncated - exceeded 10MB limit]\n")
        });
    }

    private async Task<string> FormatResponseIfNeeded(string content, HttpResponseMessage response, bool wasTruncated)
    {
        if (wasTruncated || 
            content.Length >= 1_000_000 || 
            !IsJsonContent(response))
            return content;

        return await TryFormatAsJson(content);
    }

    private static bool IsJsonContent(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentType?.MediaType?.Contains("json") == true;
    }

    private async Task<string> TryFormatAsJson(string content)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var document = JsonDocument.Parse(content);
                return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return content;
            }
        });
    }

    private void ReplaceResponseBodyWithFormatted(HttpRequest model, string formattedContent)
    {
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 70,
            Description = "Replace with formatted JSON",
            UiAction = () =>
            {
                // Keep headers chunk, replace body chunks
                while (model.ResponseChunks.Count > 1)
                    model.ResponseChunks.RemoveAt(1);
                model.ResponseChunks.Add(formattedContent);
            }
        });
    }

    private async Task FinalizeRequestAsync(
        HttpRequest model, 
        HttpResponseMessage response, 
        System.Net.Http.HttpRequestMessage httpRequest,
        TimeSpan elapsed,
        RequestQueueMessage queueMessage)
    {
        var responseSize = CalculateResponseSize(model);
        var fullResponse = BuildFullResponseString(model, response, httpRequest);

        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 85,
            Description = "Finalize metadata + full response string",
            UiAction = () =>
            {
                model.IsLoading = false;
                model.ResponseTime = elapsed;
                model.ResponseSize = responseSize;
                model.Response = fullResponse;
            }
        });

        QueueSuccessNotification(model, elapsed, responseSize);
        InvokeCompletionCallback(queueMessage, model, fullResponse, elapsed, responseSize);
    }

    private static long CalculateResponseSize(HttpRequest model)
    {
        return model.ResponseChunks.Sum(chunk => Encoding.UTF8.GetByteCount(chunk));
    }

    private string BuildFullResponseString(HttpRequest model, HttpResponseMessage response, System.Net.Http.HttpRequestMessage httpRequest)
    {
        var headerText = BuildResponseHeaders(response, httpRequest, model);
        var bodyText = string.Join("", model.ResponseChunks.Skip(1)); // Skip header chunk
        return headerText + bodyText;
    }

    private void QueueSuccessNotification(HttpRequest model, TimeSpan elapsed, long responseSize)
    {
        QueueNotification(new NotificationMessage
        {
            Title = "Request Completed",
            Message = $"HTTP {model.Method} completed. Time: {elapsed.TotalMilliseconds:F0} ms, Size: {responseSize / 1024.0:F1} KB",
            IsSuccess = true
        });
    }

    private void InvokeCompletionCallback(
        RequestQueueMessage queueMessage, 
        HttpRequest model, 
        string fullResponse, 
        TimeSpan elapsed, 
        long responseSize)
    {
        queueMessage.CompletionCallback?.Invoke(new HttpRequestResponseMessage
        {
            OriginalRequestId = queueMessage.Id,
            Request = model,
            Success = true,
            Response = fullResponse,
            ResponseTime = elapsed,
            ResponseSize = responseSize,
            RequestDateTime = model.RequestDateTime ?? DateTime.Now
        });
    }

    private void HandleRequestCancellation(HttpRequest model)
    {
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 95,
            Description = "Cancelled",
            UiAction = () =>
            {
                model.IsLoading = false;
                model.ResponseChunks.Add("\n[Request cancelled]\n");
                model.Response = "Request was cancelled.";
            }
        });

        QueueNotification(new NotificationMessage
        {
            Title = "Request Cancelled",
            Message = "The HTTP request was cancelled by the user.",
            IsSuccess = false
        });
    }

    private void HandleRequestError(HttpRequest model, Exception exception, TimeSpan elapsed)
    {
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 95,
            Description = "Failed",
            UiAction = () =>
            {
                model.IsLoading = false;
                model.ResponseChunks.Add($"\nError: {exception.Message}\n");
                model.Response = $"Error: {exception.Message}\n\nRequest URL: {model.FullUrl}";
            }
        });

        QueueNotification(new NotificationMessage
        {
            Title = "Request Failed",
            Message = $"HTTP request failed: {exception.Message}",
            IsSuccess = false
        });
    }

    private static void ApplyAuthentication(System.Net.Http.HttpRequestMessage request, AuthenticationSettings auth)
    {
        switch (auth.Type)
        {
            case AuthenticationType.BasicAuth:
                if (!string.IsNullOrWhiteSpace(auth.Username))
                {
                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}"));
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }
                break;
            case AuthenticationType.BearerToken:
                if (!string.IsNullOrWhiteSpace(auth.Token))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);
                }
                break;
            case AuthenticationType.ApiKey:
                if (!string.IsNullOrWhiteSpace(auth.ApiKeyHeader) && !string.IsNullOrWhiteSpace(auth.ApiKeyValue))
                {
                    request.Headers.TryAddWithoutValidation(auth.ApiKeyHeader, auth.ApiKeyValue);
                }
                break;
            case AuthenticationType.None:
            default:
                break;
        }
    }

    private string BuildResponseHeaders(HttpResponseMessage response, System.Net.Http.HttpRequestMessage request, HttpRequest httpRequest)
    {
        var responseText = new StringBuilder(512);
        responseText.AppendLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        responseText.AppendLine($"Request URL: {request.RequestUri}");
        if (httpRequest.Authentication.Type != AuthenticationType.None)
        {
            responseText.AppendLine($"Authentication: {httpRequest.Authentication.Type}");
        }
        responseText.AppendLine();
        responseText.AppendLine("Response Headers:");
        foreach (var header in response.Headers)
            responseText.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        foreach (var header in response.Content.Headers)
            responseText.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        responseText.AppendLine();
        responseText.AppendLine("Response Body:");
        return responseText.ToString();
    }

    private async Task ProcessUiUpdatesAsync()
    {
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var message = await _uiUpdateQueue.DequeueAsync(_cancellationTokenSource.Token);
                if (message == null) continue;
                await _dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        switch (message)
                        {
                            case UiUpdateMessage updateMessage:
                                updateMessage.UiAction?.Invoke();
                                break;
                            case NotificationMessage notificationMessage:
                                ShowNotification(notificationMessage);
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error executing UI update: {ex}");
                    }
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in UI update processor: {ex}");
            }
        }
    }

    private void ShowNotification(NotificationMessage notification)
    {
        try
        {
            var timestampedNotification = $"[{DateTime.Now:HH:mm:ss}] {notification.Title}: {notification.Message}";
            System.Diagnostics.Debug.WriteLine(timestampedNotification);
            notification.OnShown?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error showing notification: {ex}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cancellationTokenSource.Cancel();
        try
        {
            Task.WaitAll(new[] { _httpRequestProcessor, _uiUpdateProcessor }.Where(t => t != null).ToArray()!, TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error waiting for processors to complete: {ex}");
        }
        _httpRequestQueue.Dispose();
        _uiUpdateQueue.Dispose();
        _cancellationTokenSource.Dispose();
    }
}