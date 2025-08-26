using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;
using Newtonsoft.Json;
using System.Threading;
using System.IO;
using System.Windows.Threading;

namespace APIHammerUI.Views
{
    public partial class HttpRequestView : UserControl
    {
        private static readonly HttpClient httpClient = new HttpClient()
        {
            MaxResponseContentBufferSize = MAX_RESPONSE_SIZE,
            Timeout = TimeSpan.FromMinutes(5) // 5 minute timeout
        };
        private CancellationTokenSource? _currentRequestCancellation;
        private DispatcherTimer? _uiUpdateTimer;
        private volatile string? _currentResponseContent;
        private volatile bool _isStreamingComplete;

        // Memory management constants
        private const int MAX_RESPONSE_SIZE = 10 * 1024 * 1024; // 10MB limit
        private const int BUFFER_SIZE = 16384; // 16KB buffer
        private const int UI_UPDATE_INTERVAL_MS = 1000; // Update UI every 1 second
        private const int LARGE_RESPONSE_THRESHOLD = 1024 * 1024; // 1MB threshold

        static HttpRequestView()
        {
            // Configure HttpClient for better performance
            httpClient.DefaultRequestHeaders.Add("User-Agent", "APIHammer/1.0");
        }

        public HttpRequestView()
        {
            InitializeComponent();
            
            // Subscribe to the Unloaded event for cleanup
            this.Unloaded += HttpRequestView_Unloaded;
        }

        private void HttpRequestView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Cleanup when control is unloaded
            _currentRequestCancellation?.Cancel();
            _uiUpdateTimer?.Stop();
        }

        private void AddHeader_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is HttpRequest httpRequest)
            {
                httpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
            }
        }

        private void DeleteHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is HttpHeaderItem headerItem && DataContext is HttpRequest httpRequest)
            {
                // Don't allow deleting if it's the last header or if there are no headers
                if (httpRequest.Headers.Count > 1)
                {
                    httpRequest.Headers.Remove(headerItem);
                }
            }
        }

        private void AddQueryParameter_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is HttpRequest httpRequest)
            {
                httpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
            }
        }

        private void DeleteQueryParameter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is HttpQueryParameter queryParam && DataContext is HttpRequest httpRequest)
            {
                // Don't allow deleting if it's the last query parameter
                if (httpRequest.QueryParameters.Count > 1)
                {
                    httpRequest.QueryParameters.Remove(queryParam);
                }
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is HttpRequest httpRequest)
            {
                // Auto-add a new empty header row when user focuses on the last empty row
                var lastHeader = httpRequest.Headers.LastOrDefault();
                if (lastHeader != null && !string.IsNullOrWhiteSpace(lastHeader.Key) && !string.IsNullOrWhiteSpace(lastHeader.Value))
                {
                    httpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
                }
            }
        }

        private void QueryParameter_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox && DataContext is HttpRequest httpRequest)
            {
                // Auto-add a new empty query parameter row when user focuses on the last empty row
                var lastParam = httpRequest.QueryParameters.LastOrDefault();
                if (lastParam != null && !string.IsNullOrWhiteSpace(lastParam.Key) && !string.IsNullOrWhiteSpace(lastParam.Value))
                {
                    httpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
                }
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && DataContext is HttpRequest httpRequest)
            {
                httpRequest.Authentication.Password = passwordBox.Password;
            }
        }

        private void QuickAddHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string headerName && DataContext is HttpRequest httpRequest)
            {
                // Check if header already exists
                var existingHeader = httpRequest.Headers.FirstOrDefault(h => 
                    h.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase));

                if (existingHeader != null)
                {
                    // Header exists, just enable it and focus on value field
                    existingHeader.IsEnabled = true;
                    return;
                }

                // Find the first empty header row or create a new one
                var emptyHeader = httpRequest.Headers.FirstOrDefault(h => string.IsNullOrWhiteSpace(h.Key));
                
                if (emptyHeader != null)
                {
                    // Use existing empty row
                    emptyHeader.Key = headerName;
                    emptyHeader.Value = GetDefaultValueForHeader(headerName);
                    emptyHeader.IsEnabled = true;
                }
                else
                {
                    // Create new header
                    var newHeader = new HttpHeaderItem 
                    { 
                        Key = headerName, 
                        Value = GetDefaultValueForHeader(headerName),
                        IsEnabled = true 
                    };
                    
                    // Insert before the last empty row if it exists
                    var lastEmptyIndex = httpRequest.Headers.Count - 1;
                    if (lastEmptyIndex >= 0 && string.IsNullOrWhiteSpace(httpRequest.Headers[lastEmptyIndex].Key))
                    {
                        httpRequest.Headers.Insert(lastEmptyIndex, newHeader);
                    }
                    else
                    {
                        httpRequest.Headers.Add(newHeader);
                        // Add a new empty row
                        httpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "" });
                    }
                }
            }
        }

        private string GetDefaultValueForHeader(string headerName)
        {
            // Get suggested values from the model
            if (HttpRequest.HeaderValueSuggestions.TryGetValue(headerName, out var suggestions) && suggestions.Any())
            {
                return suggestions.First();
            }

            // Provide some smart defaults
            return headerName switch
            {
                "Content-Type" => "application/json",
                "Accept" => "application/json",
                "Accept-Encoding" => "gzip, deflate",
                "Cache-Control" => "no-cache",
                "Connection" => "keep-alive",
                "User-Agent" => "API Hammer/1.0",
                "X-Requested-With" => "XMLHttpRequest",
                "Access-Control-Allow-Origin" => "*",
                "Access-Control-Allow-Methods" => "GET, POST, PUT, DELETE",
                "Access-Control-Allow-Headers" => "Content-Type, Authorization",
                _ => ""
            };
        }

        private void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is HttpRequest httpRequest)
            {
                try
                {
                    var previewDialog = new HttpRequestPreviewDialog(httpRequest)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    previewDialog.ShowDialog();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening request preview: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest)
                return;

            // If currently loading, cancel the request
            if (httpRequest.IsLoading)
            {
                _currentRequestCancellation?.Cancel();
                _uiUpdateTimer?.Stop();
                httpRequest.IsLoading = false;
                httpRequest.Response = "Request cancelled by user.";
                return;
            }

            // Cancel any existing request
            _currentRequestCancellation?.Cancel();
            _uiUpdateTimer?.Stop();
            _currentRequestCancellation = new CancellationTokenSource();

            // Start the request in a fire-and-forget manner to avoid blocking the UI
            _ = Task.Run(async () => await SendHttpRequestAsync(httpRequest, _currentRequestCancellation.Token));
        }

        private async Task SendHttpRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
        {
            HttpRequestMessage? request = null;
            HttpResponseMessage? response = null;
            Stream? responseStream = null;
            
            try
            {
                // Reset state
                _currentResponseContent = null;
                _isStreamingComplete = false;

                // Update UI on the UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.IsLoading = true;
                    httpRequest.Response = "Sending request...";
                    
                    // Reset response metadata
                    httpRequest.ResponseTime = null;
                    httpRequest.ResponseSize = null;
                    httpRequest.RequestDateTime = DateTime.Now;
                }, DispatcherPriority.Background);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                request = new HttpRequestMessage();
                
                // Set method
                request.Method = httpRequest.Method switch
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

                // Use the FullUrl which includes query parameters
                if (!Uri.TryCreate(httpRequest.FullUrl, UriKind.Absolute, out var uri))
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        httpRequest.Response = "Invalid URL format. Please check your base URL and query parameters.";
                        httpRequest.IsLoading = false;
                    }, DispatcherPriority.Background);
                    return;
                }

                request.RequestUri = uri;

                // Apply authentication
                ApplyAuthentication(request, httpRequest.Authentication);

                // Set headers from the dynamic header collection
                foreach (var headerItem in httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
                {
                    try
                    {
                        // Skip Authorization header if it's already set by authentication
                        if (headerItem.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && 
                            (httpRequest.Authentication.Type == AuthenticationType.BasicAuth || 
                             httpRequest.Authentication.Type == AuthenticationType.BearerToken))
                            continue;

                        // Try to add to request headers first
                        request.Headers.TryAddWithoutValidation(headerItem.Key, headerItem.Value);
                    }
                    catch
                    {
                        // If it fails, it might be a content header, we'll handle it after creating content
                    }
                }

                // Set body
                if (!string.IsNullOrWhiteSpace(httpRequest.Body) && 
                    (httpRequest.Method == "POST" || httpRequest.Method == "PUT" || httpRequest.Method == "PATCH"))
                {
                    // Determine content type from headers or default to JSON
                    var contentType = httpRequest.Headers
                        .FirstOrDefault(h => h.IsEnabled && h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        ?.Value ?? "application/json";

                    request.Content = new StringContent(httpRequest.Body, Encoding.UTF8, contentType);

                    // Add content-specific headers
                    foreach (var headerItem in httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
                    {
                        if (IsContentHeader(headerItem.Key))
                        {
                            try
                            {
                                request.Content.Headers.TryAddWithoutValidation(headerItem.Key, headerItem.Value);
                            }
                            catch
                            {
                                // Ignore invalid content headers
                            }
                        }
                    }
                }

                // Update UI to show that request is being sent
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.Response = "Request sent, waiting for response...";
                }, DispatcherPriority.Background);

                // Send the request with streaming support
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                stopwatch.Stop();
                
                // Capture response metadata
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.ResponseTime = stopwatch.Elapsed;
                }, DispatcherPriority.Background);

                // Build response headers
                var responseHeaders = BuildResponseHeaders(response, request, httpRequest);

                // Check response size first
                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MAX_RESPONSE_SIZE)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        httpRequest.Response = responseHeaders + 
                            $"\nResponse too large ({contentLength.Value / (1024 * 1024):F1} MB). " +
                            "Maximum supported size is 10 MB.\n" +
                            "Consider using a streaming client for large responses.";
                        httpRequest.ResponseSize = contentLength.Value;
                    }, DispatcherPriority.Background);
                    return;
                }

                // Stream the response content efficiently
                responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                
                // Start UI update timer for large responses
                var isLargeResponse = contentLength.HasValue && contentLength.Value > LARGE_RESPONSE_THRESHOLD;
                if (isLargeResponse)
                {
                    StartProgressTimer(httpRequest, responseHeaders);
                }

                // Read response content efficiently
                await ReadResponseContentAsync(responseStream, response, httpRequest, responseHeaders, cancellationToken);

                // Show success notification
                await ShowNotificationAsync("Request Completed", 
                    $"HTTP {httpRequest.Method} request completed successfully\n" +
                    $"Status: {(int)response.StatusCode} {response.StatusCode}\n" +
                    $"Time: {httpRequest.ResponseTimeFormatted}\n" +
                    $"Size: {httpRequest.ResponseSizeFormatted}", 
                    isSuccess: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.Response = "Request was cancelled.";
                }, DispatcherPriority.Background);

                await ShowNotificationAsync("Request Cancelled", 
                    "The HTTP request was cancelled by the user.", 
                    isSuccess: false);
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}";
                }, DispatcherPriority.Background);

                await ShowNotificationAsync("Request Failed", 
                    $"HTTP request failed: {ex.Message}", 
                    isSuccess: false);
            }
            finally
            {
                // Cleanup
                _uiUpdateTimer?.Stop();
                _isStreamingComplete = true;
                
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.IsLoading = false;
                }, DispatcherPriority.Background);

                // Dispose resources
                responseStream?.Dispose();
                response?.Dispose();
                request?.Dispose();
            }
        }

        private void StartProgressTimer(HttpRequest httpRequest, string responseHeaders)
        {
            _uiUpdateTimer?.Stop();
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UI_UPDATE_INTERVAL_MS)
            };

            _uiUpdateTimer.Tick += (s, e) =>
            {
                if (_isStreamingComplete)
                {
                    _uiUpdateTimer.Stop();
                    return;
                }

                var currentContent = _currentResponseContent;
                if (currentContent != null)
                {
                    var progressInfo = $"\n\nStreaming... ({Encoding.UTF8.GetByteCount(currentContent) / 1024:F1} KB received)";
                    httpRequest.Response = responseHeaders + currentContent + progressInfo;
                }
            };

            _uiUpdateTimer.Start();
        }

        private async Task ReadResponseContentAsync(Stream responseStream, HttpResponseMessage response, 
            HttpRequest httpRequest, string responseHeaders, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(responseStream, Encoding.UTF8, leaveOpen: false);
            var contentBuilder = new StringBuilder();
            var buffer = new char[BUFFER_SIZE];
            int bytesRead;
            long totalBytesRead = 0;

            try
            {
                while ((bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    contentBuilder.Append(buffer, 0, bytesRead);
                    totalBytesRead += bytesRead * sizeof(char);

                    // Update volatile field for timer
                    _currentResponseContent = contentBuilder.ToString();

                    // Check if we've hit the memory limit
                    if (totalBytesRead > MAX_RESPONSE_SIZE)
                    {
                        contentBuilder.AppendLine("\n\n[Response truncated - exceeded 10MB limit]");
                        break;
                    }

                    // Force garbage collection for very large responses to prevent memory pressure
                    if (totalBytesRead % (2 * 1024 * 1024) == 0) // Every 2MB
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                contentBuilder.Clear();
                contentBuilder.AppendLine("[Response too large - out of memory]");
                
                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            _isStreamingComplete = true;
            _uiUpdateTimer?.Stop();

            // Final processing
            var finalContent = contentBuilder.ToString();
            var formattedContent = await FormatResponseContentAsync(finalContent, response);

            await Dispatcher.InvokeAsync(() =>
            {
                httpRequest.ResponseSize = Encoding.UTF8.GetByteCount(finalContent);
                httpRequest.Response = responseHeaders + formattedContent;
            }, DispatcherPriority.Background);

            // Clear builder to free memory immediately
            contentBuilder.Clear();
            contentBuilder = null;
        }

        private async Task<string> FormatResponseContentAsync(string content, HttpResponseMessage response)
        {
            // Don't format very large responses to avoid memory issues
            if (content.Length > LARGE_RESPONSE_THRESHOLD)
            {
                return content;
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
                    {
                        // Use more memory-efficient JSON formatting
                        using var stringReader = new StringReader(content);
                        using var jsonReader = new JsonTextReader(stringReader);
                        using var stringWriter = new StringWriter();
                        using var jsonWriter = new JsonTextWriter(stringWriter) { Formatting = Formatting.Indented };

                        jsonWriter.WriteToken(jsonReader);
                        return stringWriter.ToString();
                    }
                }
                catch
                {
                    // Return original content if formatting fails
                }

                return content;
            });
        }

        private string BuildResponseHeaders(HttpResponseMessage response, HttpRequestMessage request, HttpRequest httpRequest)
        {
            var responseText = new StringBuilder(1024);
            responseText.AppendLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
            responseText.AppendLine($"Request URL: {request.RequestUri}");
            
            // Show authentication info (without sensitive data)
            if (httpRequest.Authentication.Type != AuthenticationType.None)
            {
                responseText.AppendLine($"Authentication: {httpRequest.Authentication.Type}");
            }
            
            responseText.AppendLine();
            responseText.AppendLine("Response Headers:");
            
            foreach (var header in response.Headers)
            {
                responseText.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }
            
            foreach (var header in response.Content.Headers)
            {
                responseText.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            responseText.AppendLine();
            responseText.AppendLine("Response Body:");

            return responseText.ToString();
        }

        private async Task ShowNotificationAsync(string title, string message, bool isSuccess)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                // Simple non-blocking notification by prepending to the response
                var timestampedNotification = $"[{DateTime.Now:HH:mm:ss}] {title}: {message}\n\n";
                
                if (DataContext is HttpRequest httpRequest)
                {
                    // Prepend notification to the existing response without excessive string operations
                    var currentResponse = httpRequest.Response ?? "";
                    if (currentResponse.Length < 1000) // Only prepend for small responses to avoid memory issues
                    {
                        httpRequest.Response = timestampedNotification + currentResponse;
                    }
                }
                
                // Also log to debug output for development
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {title}: {message}");
            }, DispatcherPriority.Background);
        }

        private static void ApplyAuthentication(HttpRequestMessage request, AuthenticationSettings auth)
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
                    // No authentication
                    break;
            }
        }

        private static bool IsContentHeader(string headerName)
        {
            // Content headers that should be set on HttpContent rather than HttpRequestMessage
            var contentHeaders = new[]
            {
                "Content-Type", "Content-Length", "Content-Encoding", "Content-Language",
                "Content-Location", "Content-MD5", "Content-Range", "Expires", "Last-Modified"
            };

            return contentHeaders.Any(h => h.Equals(headerName, StringComparison.OrdinalIgnoreCase));
        }
    }
}