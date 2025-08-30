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
        var ct = linkedCts.Token;
        var model = queueMessage.Request;

        // Initial UI state
        QueueUiUpdate(new UiUpdateMessage
        {
            Priority = 95,
            Description = "Init streaming state",
            UiAction = () =>
            {
                model.IsLoading = true;
                model.RequestDateTime = DateTime.Now;
                model.ResponseTime = null;
                model.ResponseSize = null;
                model.Response = string.Empty; // final concatenated later
                model.ResponseChunks.Clear();
                model.ResponseChunks.Add("(Connecting...)\n");
            }
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        System.Net.Http.HttpRequestMessage? httpReq = null;
        HttpResponseMessage? response = null;
        Stream? stream = null;

        try
        {
            httpReq = new System.Net.Http.HttpRequestMessage
            {
                Method = model.Method switch
                {
                    "GET" => HttpMethod.Get,
                    "POST" => HttpMethod.Post,
                    "PUT" => HttpMethod.Put,
                    "DELETE" => HttpMethod.Delete,
                    "PATCH" => HttpMethod.Patch,
                    "HEAD" => HttpMethod.Head,
                    "OPTIONS" => HttpMethod.Options,
                    _ => HttpMethod.Get
                }
            };

            if (!Uri.TryCreate(model.FullUrl, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Invalid URL format.");
            httpReq.RequestUri = uri;

            ApplyAuthentication(httpReq, model.Authentication);
            foreach (var hdr in model.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
            {
                if (hdr.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                    (model.Authentication.Type == AuthenticationType.BasicAuth || model.Authentication.Type == AuthenticationType.BearerToken))
                    continue;
                httpReq.Headers.TryAddWithoutValidation(hdr.Key, hdr.Value);
            }
            if (!string.IsNullOrWhiteSpace(model.Body) && model.Method is "POST" or "PUT" or "PATCH")
            {
                var contentType = model.Headers.FirstOrDefault(h => h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? "application/json";
                httpReq.Content = new StringContent(model.Body, Encoding.UTF8, contentType);
            }

            response = await _streamHttpClient.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);

            var headerText = BuildResponseHeaders(response, httpReq, model);
            QueueUiUpdate(new UiUpdateMessage
            {
                Priority = 90,
                Description = "Add headers chunk",
                UiAction = () =>
                {
                    model.ResponseChunks.Clear();
                    model.ResponseChunks.Add(headerText); // header includes "Response Body:" line
                }
            });

            stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, STREAM_BUFFER_SIZE, leaveOpen: true);
            var buffer = new char[STREAM_BUFFER_SIZE];
            int read;
            long totalBytes = 0;
            var sb = new StringBuilder();
            int lastEmittedLength = 0;
            DateTime lastEmit = DateTime.UtcNow;
            bool truncated = false;

            while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                sb.Append(buffer, 0, read);
                totalBytes += Encoding.UTF8.GetByteCount(buffer, 0, read);

                if (totalBytes > MAX_RESPONSE_SIZE)
                {
                    truncated = true;
                    break;
                }

                var now = DateTime.UtcNow;
                if ((now - lastEmit).TotalMilliseconds >= UI_UPDATE_INTERVAL_MS)
                {
                    var newLen = sb.Length;
                    if (newLen > lastEmittedLength)
                    {
                        var slice = sb.ToString(lastEmittedLength, newLen - lastEmittedLength);
                        lastEmittedLength = newLen;
                        QueueUiUpdate(new UiUpdateMessage
                        {
                            Priority = 60,
                            Description = "Append streaming slice",
                            UiAction = () =>
                            {
                                model.ResponseChunks.Add(slice);
                            }
                        });
                    }
                    lastEmit = now;
                }
            }

            // Emit remaining slice
            if (sb.Length > lastEmittedLength)
            {
                var slice = sb.ToString(lastEmittedLength, sb.Length - lastEmittedLength);
                QueueUiUpdate(new UiUpdateMessage
                {
                    Priority = 60,
                    Description = "Append final streaming slice",
                    UiAction = () => model.ResponseChunks.Add(slice)
                });
            }

            sw.Stop();
            if (truncated)
            {
                QueueUiUpdate(new UiUpdateMessage
                {
                    Priority = 55,
                    Description = "Add truncated notice",
                    UiAction = () => model.ResponseChunks.Add("\n[Response truncated - exceeded 10MB limit]\n")
                });
            }

            // Optionally JSON format (only if small) -> replace body part
            string rawBody = sb.ToString();
            string finalBody = rawBody;
            bool formatted = false;
            if (!truncated && rawBody.Length < 1_000_000 && response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    finalBody = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                    formatted = true;
                }
                catch { }
            }

            if (formatted)
            {
                // Replace existing body chunks (keep first headers chunk)
                QueueUiUpdate(new UiUpdateMessage
                {
                    Priority = 70,
                    Description = "Replace with formatted JSON",
                    UiAction = () =>
                    {
                        while (model.ResponseChunks.Count > 1)
                            model.ResponseChunks.RemoveAt(1);
                        model.ResponseChunks.Add(finalBody);
                    }
                });
            }

            var elapsed = sw.Elapsed;
            var sizeBytes = Math.Min(totalBytes, MAX_RESPONSE_SIZE);
            var fullResponse = headerText + finalBody + (truncated ? "\n[Response truncated - exceeded 10MB limit]\n" : string.Empty);

            QueueUiUpdate(new UiUpdateMessage
            {
                Priority = 85,
                Description = "Finalize metadata + full response string",
                UiAction = () =>
                {
                    model.IsLoading = false;
                    model.ResponseTime = elapsed;
                    model.ResponseSize = sizeBytes;
                    model.Response = fullResponse; // for save / copy
                }
            });

            QueueNotification(new NotificationMessage
            {
                Title = "Request Completed",
                Message = $"HTTP {model.Method} completed. Time: {elapsed.TotalMilliseconds:F0} ms, Size: {sizeBytes / 1024.0:F1} KB",
                IsSuccess = true
            });

            queueMessage.CompletionCallback?.Invoke(new HttpRequestResponseMessage
            {
                OriginalRequestId = queueMessage.Id,
                Request = model,
                Success = true,
                Response = fullResponse,
                ResponseTime = elapsed,
                ResponseSize = sizeBytes,
                RequestDateTime = model.RequestDateTime ?? DateTime.Now
            });
        }
        catch (OperationCanceledException) when (queueMessage.CancellationToken.IsCancellationRequested)
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
        catch (Exception ex)
        {
            sw.Stop();
            QueueUiUpdate(new UiUpdateMessage
            {
                Priority = 95,
                Description = "Failed",
                UiAction = () =>
                {
                    model.IsLoading = false;
                    model.ResponseChunks.Add($"\nError: {ex.Message}\n");
                    model.Response = $"Error: {ex.Message}\n\nRequest URL: {model.FullUrl}";
                }
            });
            QueueNotification(new NotificationMessage
            {
                Title = "Request Failed",
                Message = $"HTTP request failed: {ex.Message}",
                IsSuccess = false
            });
        }
        finally
        {
            stream?.Dispose();
            response?.Dispose();
            httpReq?.Dispose();
        }
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