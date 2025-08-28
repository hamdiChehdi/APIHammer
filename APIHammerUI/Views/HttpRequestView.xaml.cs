using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;
using System.Threading;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Text.Json;

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
        private bool _isDisposing = false;

        // Memory management constants
        private const int MAX_RESPONSE_SIZE = 10 * 1024 * 1024; // 10MB limit

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
            
            // Add keyboard shortcuts
            this.KeyDown += HttpRequestView_KeyDown;
        }

        private void HttpRequestView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Only cleanup UI resources when unloaded, but don't cancel ongoing requests
            // This allows tabs to work independently - switching tabs won't cancel requests
            _uiUpdateTimer?.Stop();
            
            // Only cancel request if the control is being permanently disposed
            // This happens when the tab is closed, not when switching between tabs
            if (_isDisposing)
            {
                _currentRequestCancellation?.Cancel();
            }
        }

        /// <summary>
        /// Call this method when the tab is being permanently closed
        /// </summary>
        public void Dispose()
        {
            _isDisposing = true;
            _currentRequestCancellation?.Cancel();
            _uiUpdateTimer?.Stop();
        }

        private void HttpRequestView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest || string.IsNullOrWhiteSpace(httpRequest.Response))
                return;

            // Ctrl+S for Save as Text
            if (e.Key == System.Windows.Input.Key.S && 
                (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                SaveAsText_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+J for Save as JSON
            else if (e.Key == System.Windows.Input.Key.J && 
                     (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
            {
                SaveAsJson_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+C for Copy (only if not in a text input)
            else if (e.Key == System.Windows.Input.Key.C && 
                     (e.KeyboardDevice.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0 &&
                     !(e.OriginalSource is TextBox textBox && !textBox.IsReadOnly))
            {
                CopyResponse_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
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

            // Cancel any existing request for this tab
            _currentRequestCancellation?.Cancel();
            _uiUpdateTimer?.Stop();
            _currentRequestCancellation = new CancellationTokenSource();

            // Start the request using the HttpRequestService
            _ = Task.Run(async () => await SendHttpRequestWithServiceAsync(httpRequest, _currentRequestCancellation.Token));
        }

        private async Task SendHttpRequestWithServiceAsync(HttpRequest httpRequest, CancellationToken cancellationToken)
        {
            try
            {
                // Update UI on the UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    httpRequest.IsLoading = true;
                    httpRequest.Response = "Sending request...";
                }, DispatcherPriority.Background);

                // Use the HttpRequestService
                var httpRequestService = new APIHammerUI.Services.HttpRequestService();
                var result = await httpRequestService.SendRequestAsync(httpRequest, cancellationToken);

                // Check if cancellation was requested before updating UI
                if (cancellationToken.IsCancellationRequested)
                    return;

                // Update UI with results
                await Dispatcher.InvokeAsync(() =>
                {
                    // Double-check we haven't been cancelled while waiting for UI thread
                    if (cancellationToken.IsCancellationRequested)
                        return;
                        
                    httpRequest.IsLoading = false;
                    httpRequest.Response = result.Response;
                    httpRequest.ResponseTime = result.ResponseTime;
                    httpRequest.ResponseSize = result.ResponseSize;
                    httpRequest.RequestDateTime = result.RequestDateTime;
                }, DispatcherPriority.Background);

                // Show notification only if not cancelled
                if (!cancellationToken.IsCancellationRequested)
                {
                    var title = result.Success ? "Request Completed" : "Request Failed";
                    var message = result.Success 
                        ? $"HTTP {httpRequest.Method} request completed successfully\n" +
                          $"Time: {httpRequest.ResponseTimeFormatted}\n" +
                          $"Size: {httpRequest.ResponseSizeFormatted}"
                        : $"HTTP request failed: {result.ErrorMessage}";

                    await ShowNotificationAsync(title, message, result.Success);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only update UI if the control is still loaded and hasn't been disposed
                if (!_isDisposing && IsLoaded)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        httpRequest.IsLoading = false;
                        httpRequest.Response = "Request was cancelled.";
                    }, DispatcherPriority.Background);

                    await ShowNotificationAsync("Request Cancelled", 
                        "The HTTP request was cancelled by the user.", 
                        isSuccess: false);
                }
            }
            catch (Exception ex)
            {
                // Only update UI if the control is still loaded and hasn't been disposed
                if (!_isDisposing && IsLoaded)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        httpRequest.IsLoading = false;
                        httpRequest.Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}";
                    }, DispatcherPriority.Background);

                    await ShowNotificationAsync("Request Failed", 
                        $"HTTP request failed: {ex.Message}", 
                        isSuccess: false);
                }
            }
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

        private void SaveAsJson_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest || string.IsNullOrWhiteSpace(httpRequest.Response))
                return;

            try
            {
                // Extract JSON content from response
                var jsonContent = ExtractResponseBody(httpRequest.Response);
                
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    MessageBox.Show("No response body found to save.", "Save JSON", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validate and format JSON
                string formattedJson;
                bool isValidJson = true;
                try
                {
                    using var document = JsonDocument.Parse(jsonContent);
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    formattedJson = JsonSerializer.Serialize(document, options);
                }
                catch (JsonException)
                {
                    isValidJson = false;
                    // If it's not valid JSON, save as-is but warn the user
                    var result = MessageBox.Show(
                        "The response body doesn't appear to be valid JSON. Save as plain text instead?", 
                        "Invalid JSON", 
                        MessageBoxButton.YesNo, 
                        MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                        return;
                    
                    formattedJson = jsonContent;
                }

                // Show save dialog
                var extension = isValidJson ? ".json" : ".txt";
                var filter = isValidJson ? 
                    "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*" :
                    "Text Files (*.txt)|*.txt|JSON Files (*.json)|*.json|All Files (*.*)|*.*";

                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Response Body",
                    Filter = filter,
                    DefaultExt = extension,
                    FileName = GenerateFileName("response_body", extension)
                };

                if (saveDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveDialog.FileName, formattedJson, Encoding.UTF8);
                    
                    var fileInfo = new FileInfo(saveDialog.FileName);
                    var sizeInfo = fileInfo.Length < 1024 ? 
                        $"{fileInfo.Length} bytes" : 
                        $"{fileInfo.Length / 1024.0:F1} KB";

                    MessageBox.Show($"Response body saved successfully!\n\nFile: {saveDialog.FileName}\nSize: {sizeInfo}", 
                        "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Access denied. Please choose a different location or run as administrator.", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (DirectoryNotFoundException)
            {
                MessageBox.Show("The specified directory does not exist.", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveAsText_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest || string.IsNullOrWhiteSpace(httpRequest.Response))
                return;

            try
            {
                // Show save dialog
                var saveDialog = new SaveFileDialog
                {
                    Title = "Save Full Response as Text",
                    Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                    DefaultExt = ".txt",
                    FileName = GenerateFileName("http_response", ".txt")
                };

                if (saveDialog.ShowDialog() == true)
                {
                    // Save the complete response including headers and metadata
                    var fullResponse = BuildFullResponseForSave(httpRequest);
                    File.WriteAllText(saveDialog.FileName, fullResponse, Encoding.UTF8);
                    
                    var fileInfo = new FileInfo(saveDialog.FileName);
                    var sizeInfo = fileInfo.Length < 1024 ? 
                        $"{fileInfo.Length} bytes" : 
                        fileInfo.Length < 1024 * 1024 ?
                        $"{fileInfo.Length / 1024.0:F1} KB" :
                        $"{fileInfo.Length / (1024.0 * 1024.0):F1} MB";

                    MessageBox.Show($"Full response saved successfully!\n\nFile: {saveDialog.FileName}\nSize: {sizeInfo}", 
                        "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Access denied. Please choose a different location or run as administrator.", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (DirectoryNotFoundException)
            {
                MessageBox.Show("The specified directory does not exist.", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ExtractResponseBody(string fullResponse)
        {
            // Find the "Response Body:" section
            const string bodyMarker = "Response Body:";
            var bodyIndex = fullResponse.IndexOf(bodyMarker, StringComparison.OrdinalIgnoreCase);
            
            if (bodyIndex == -1)
                return string.Empty;

            // Extract everything after "Response Body:"
            var startIndex = bodyIndex + bodyMarker.Length;
            var responseBody = fullResponse.Substring(startIndex).Trim();
            
            // Remove any progress indicators that might be at the end
            var progressMarkers = new[] { "\n\nStreaming...", "[Response truncated", "[Response too large" };
            foreach (var marker in progressMarkers)
            {
                var markerIndex = responseBody.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex != -1)
                {
                    responseBody = responseBody.Substring(0, markerIndex).Trim();
                    break;
                }
            }

            return responseBody;
        }

        private string BuildFullResponseForSave(HttpRequest httpRequest)
        {
            var fullResponse = new StringBuilder();
            
            // Add metadata header
            fullResponse.AppendLine("=".PadRight(80, '='));
            fullResponse.AppendLine("API HAMMER - HTTP Response Export");
            fullResponse.AppendLine("=".PadRight(80, '='));
            fullResponse.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (Local Time)");
            fullResponse.AppendLine($"Tool Version: API Hammer v1.0");
            fullResponse.AppendLine();

            // Add request information
            fullResponse.AppendLine("-".PadRight(80, '-'));
            fullResponse.AppendLine("REQUEST INFORMATION");
            fullResponse.AppendLine("-".PadRight(80, '-'));
            fullResponse.AppendLine($"Method: {httpRequest.Method}");
            fullResponse.AppendLine($"URL: {httpRequest.FullUrl}");
            
            if (httpRequest.RequestDateTime.HasValue)
                fullResponse.AppendLine($"Request Sent: {httpRequest.RequestDateTime:yyyy-MM-dd HH:mm:ss.fff}");
            
            if (httpRequest.ResponseTime.HasValue)
                fullResponse.AppendLine($"Response Time: {httpRequest.ResponseTimeFormatted}");
            
            if (httpRequest.ResponseSize.HasValue)
                fullResponse.AppendLine($"Response Size: {httpRequest.ResponseSizeFormatted}");

            // Add authentication info (without sensitive data)
            if (httpRequest.Authentication.Type != AuthenticationType.None)
            {
                fullResponse.AppendLine($"Authentication Type: {httpRequest.Authentication.Type}");
                if (httpRequest.Authentication.Type == AuthenticationType.ApiKey && 
                    !string.IsNullOrWhiteSpace(httpRequest.Authentication.ApiKeyHeader))
                {
                    fullResponse.AppendLine($"API Key Header: {httpRequest.Authentication.ApiKeyHeader}");
                }
                if (httpRequest.Authentication.Type == AuthenticationType.BasicAuth && 
                    !string.IsNullOrWhiteSpace(httpRequest.Authentication.Username))
                {
                    fullResponse.AppendLine($"Username: {httpRequest.Authentication.Username}");
                }
            }

            fullResponse.AppendLine();

            // Add request headers
            var enabledHeaders = httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)).ToList();
            if (enabledHeaders.Any())
            {
                fullResponse.AppendLine("-".PadRight(80, '-'));
                fullResponse.AppendLine("REQUEST HEADERS");
                fullResponse.AppendLine("-".PadRight(80, '-'));
                
                foreach (var header in enabledHeaders)
                {
                    fullResponse.AppendLine($"{header.Key}: {header.Value}");
                }
                fullResponse.AppendLine();
            }

            // Add query parameters
            var enabledParams = httpRequest.QueryParameters.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
            if (enabledParams.Any())
            {
                fullResponse.AppendLine("-".PadRight(80, '-'));
                fullResponse.AppendLine("QUERY PARAMETERS");
                fullResponse.AppendLine("-".PadRight(80, '-'));
                
                foreach (var param in enabledParams)
                {
                    fullResponse.AppendLine($"{param.Key} = {param.Value}");
                }
                fullResponse.AppendLine();
            }

            // Add request body if present
            if (!string.IsNullOrWhiteSpace(httpRequest.Body))
            {
                fullResponse.AppendLine("-".PadRight(80, '-'));
                fullResponse.AppendLine("REQUEST BODY");
                fullResponse.AppendLine("-".PadRight(80, '-'));
                fullResponse.AppendLine(httpRequest.Body);
                fullResponse.AppendLine();
            }

            // Add the response section
            fullResponse.AppendLine("=".PadRight(80, '='));
            fullResponse.AppendLine("HTTP RESPONSE");
            fullResponse.AppendLine("=".PadRight(80, '='));
            
            // Add the actual response
            fullResponse.AppendLine(httpRequest.Response);

            // Add footer
            fullResponse.AppendLine();
            fullResponse.AppendLine("=".PadRight(80, '='));
            fullResponse.AppendLine("End of API Hammer Export");
            fullResponse.AppendLine("=".PadRight(80, '='));

            return fullResponse.ToString();
        }

        private string GenerateFileName(string prefix, string extension)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{prefix}_{timestamp}{extension}";
        }

        private void CopyResponse_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest || string.IsNullOrWhiteSpace(httpRequest.Response))
                return;

            try
            {
                Clipboard.SetText(httpRequest.Response);
                
                // Show a brief feedback (could be replaced with a toast notification)
                MessageBox.Show("Response copied to clipboard!", "Copy Complete", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Copy Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}