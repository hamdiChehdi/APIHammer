using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using APIHammerUI.Models;
using APIHammerUI.Services;
using APIHammerUI.Commands;
using APIHammerUI.Messages;
using System.Text;
using System.Linq;

namespace APIHammerUI.ViewModels;

public class HttpRequestViewModel : INotifyPropertyChanged
{
    private CancellationTokenSource? _currentRequestCancellation;
    
    public HttpRequest HttpRequest { get; }
    
    // Commands
    public ICommand SendRequestCommand { get; }
    public ICommand PreviewRequestCommand { get; }
    public ICommand AddHeaderCommand { get; }
    public ICommand DeleteHeaderCommand { get; }
    public ICommand AddQueryParameterCommand { get; }
    public ICommand DeleteQueryParameterCommand { get; }
    public ICommand QuickAddHeaderCommand { get; }
    public ICommand SaveAsJsonCommand { get; }
    public ICommand SaveAsTextCommand { get; }
    public ICommand CopyResponseCommand { get; }

    public HttpRequestViewModel() : this(new HttpRequest())
    {
    }

    public HttpRequestViewModel(HttpRequest httpRequest)
    {
        HttpRequest = httpRequest ?? throw new ArgumentNullException(nameof(httpRequest));

        // Initialize commands
        SendRequestCommand = new RelayCommand(ExecuteSendRequest, () => !HttpRequest.IsLoading);
        PreviewRequestCommand = new RelayCommand(ExecutePreviewRequest);
        AddHeaderCommand = new RelayCommand(ExecuteAddHeader);
        DeleteHeaderCommand = new RelayCommand<HttpHeaderItem>(ExecuteDeleteHeader, CanDeleteHeader);
        AddQueryParameterCommand = new RelayCommand(ExecuteAddQueryParameter);
        DeleteQueryParameterCommand = new RelayCommand<HttpQueryParameter>(ExecuteDeleteQueryParameter, CanDeleteQueryParameter);
        QuickAddHeaderCommand = new RelayCommand<string>(ExecuteQuickAddHeader);
        SaveAsJsonCommand = new RelayCommand(ExecuteSaveAsJson, () => !string.IsNullOrWhiteSpace(HttpRequest.Response));
        SaveAsTextCommand = new RelayCommand(ExecuteSaveAsText, () => !string.IsNullOrWhiteSpace(HttpRequest.Response));
        CopyResponseCommand = new RelayCommand(ExecuteCopyResponse, () => !string.IsNullOrWhiteSpace(HttpRequest.Response));

        // Subscribe to property changes to update command states
        HttpRequest.PropertyChanged += OnHttpRequestPropertyChanged;
    }

    private void OnHttpRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HttpRequest.IsLoading))
        {
            (SendRequestCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        else if (e.PropertyName == nameof(HttpRequest.Response))
        {
            (SaveAsJsonCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SaveAsTextCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyResponseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteSendRequest()
    {
        try
        {
            // If currently loading, cancel the request
            if (HttpRequest.IsLoading)
            {
                _currentRequestCancellation?.Cancel();
                HttpRequest.IsLoading = false;
                HttpRequest.Response = "Request cancelled by user.";
                return;
            }

            // Cancel any existing request
            _currentRequestCancellation?.Cancel();
            _currentRequestCancellation = new CancellationTokenSource();

            // Set loading state immediately on UI thread
            HttpRequest.IsLoading = true;
            HttpRequest.Response = "Queuing request...";

            // Create and queue the HTTP request message
            var requestMessage = new APIHammerUI.Messages.HttpRequestMessage
            {
                Request = HttpRequest,
                CancellationToken = _currentRequestCancellation.Token,
                Priority = 0 // Normal priority
            };

            // Queue the request for processing
            ApplicationServiceManager.Instance.MessageQueue.QueueHttpRequest(requestMessage);

            // Update UI to show request is queued
            ApplicationServiceManager.Instance.MessageQueue.QueueUiUpdate(new UiUpdateMessage
            {
                Priority = 100, // High priority for immediate UI feedback
                Description = "Update request status to processing",
                UiAction = () =>
                {
                    HttpRequest.Response = "Processing request...";
                }
            });
        }
        catch (Exception ex)
        {
            HttpRequest.IsLoading = false;
            HttpRequest.Response = $"Error queuing request: {ex.Message}";
            
            MessageBox.Show($"Error sending request: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecutePreviewRequest()
    {
        try
        {
            var previewDialog = new HttpRequestPreviewDialog(HttpRequest)
            {
                Owner = Application.Current.MainWindow
            };
            previewDialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening request preview: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteAddHeader()
    {
        HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
    }

    private void ExecuteDeleteHeader(HttpHeaderItem? headerItem)
    {
        if (headerItem != null && CanDeleteHeader(headerItem))
        {
            HttpRequest.Headers.Remove(headerItem);
        }
    }

    private bool CanDeleteHeader(HttpHeaderItem? headerItem)
    {
        return headerItem != null && HttpRequest.Headers.Count > 1;
    }

    private void ExecuteAddQueryParameter()
    {
        HttpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
    }

    private void ExecuteDeleteQueryParameter(HttpQueryParameter? queryParam)
    {
        if (queryParam != null && CanDeleteQueryParameter(queryParam))
        {
            HttpRequest.QueryParameters.Remove(queryParam);
        }
    }

    private bool CanDeleteQueryParameter(HttpQueryParameter? queryParam)
    {
        return queryParam != null && HttpRequest.QueryParameters.Count > 1;
    }

    private void ExecuteQuickAddHeader(string? headerName)
    {
        if (string.IsNullOrEmpty(headerName)) return;

        // Check if header already exists
        var existingHeader = HttpRequest.Headers.FirstOrDefault(h => 
            h.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase));

        if (existingHeader != null)
        {
            // Header exists, just enable it
            existingHeader.IsEnabled = true;
            return;
        }

        // Find the first empty header row or create a new one
        var emptyHeader = HttpRequest.Headers.FirstOrDefault(h => string.IsNullOrWhiteSpace(h.Key));
        
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
            var lastEmptyIndex = HttpRequest.Headers.Count - 1;
            if (lastEmptyIndex >= 0 && string.IsNullOrWhiteSpace(HttpRequest.Headers[lastEmptyIndex].Key))
            {
                HttpRequest.Headers.Insert(lastEmptyIndex, newHeader);
            }
            else
            {
                HttpRequest.Headers.Add(newHeader);
                // Add a new empty row
                HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "" });
            }
        }
    }

    private void ExecuteSaveAsJson()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;

        try
        {
            // Extract JSON content from response
            var jsonContent = ExtractResponseBody(HttpRequest.Response);
            
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
                var options = new JsonSerializerOptions { WriteIndented = true };
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
                
                if (result != MessageBoxResult.Yes) return;
                
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
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteSaveAsText()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;

        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Save Full Response as Text",
                Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = GenerateFileName("http_response", ".txt")
            };

            if (saveDialog.ShowDialog() == true)
            {
                var fullResponse = BuildFullResponseForSave();
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
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteCopyResponse()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;

        try
        {
            Clipboard.SetText(HttpRequest.Response);
            MessageBox.Show("Response copied to clipboard!", "Copy Complete", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Copy Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
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

    private string ExtractResponseBody(string fullResponse)
    {
        const string bodyMarker = "Response Body:";
        var bodyIndex = fullResponse.IndexOf(bodyMarker, StringComparison.OrdinalIgnoreCase);
        
        if (bodyIndex == -1) return string.Empty;

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

    private string BuildFullResponseForSave()
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
        fullResponse.AppendLine($"Method: {HttpRequest.Method}");
        fullResponse.AppendLine($"URL: {HttpRequest.FullUrl}");
        
        if (HttpRequest.RequestDateTime.HasValue)
            fullResponse.AppendLine($"Request Sent: {HttpRequest.RequestDateTime:yyyy-MM-dd HH:mm:ss.fff}");
        
        if (HttpRequest.ResponseTime.HasValue)
            fullResponse.AppendLine($"Response Time: {HttpRequest.ResponseTimeFormatted}");
        
        if (HttpRequest.ResponseSize.HasValue)
            fullResponse.AppendLine($"Response Size: {HttpRequest.ResponseSizeFormatted}");

        // Add authentication info (without sensitive data)
        if (HttpRequest.Authentication.Type != AuthenticationType.None)
        {
            fullResponse.AppendLine($"Authentication Type: {HttpRequest.Authentication.Type}");
            if (HttpRequest.Authentication.Type == AuthenticationType.ApiKey && 
                !string.IsNullOrWhiteSpace(HttpRequest.Authentication.ApiKeyHeader))
            {
                fullResponse.AppendLine($"API Key Header: {HttpRequest.Authentication.ApiKeyHeader}");
            }
            if (HttpRequest.Authentication.Type == AuthenticationType.BasicAuth && 
                !string.IsNullOrWhiteSpace(HttpRequest.Authentication.Username))
            {
                fullResponse.AppendLine($"Username: {HttpRequest.Authentication.Username}");
            }
        }

        fullResponse.AppendLine();

        // Add request headers
        var enabledHeaders = HttpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)).ToList();
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
        var enabledParams = HttpRequest.QueryParameters.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
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
        if (!string.IsNullOrWhiteSpace(HttpRequest.Body))
        {
            fullResponse.AppendLine("-".PadRight(80, '-'));
            fullResponse.AppendLine("REQUEST BODY");
            fullResponse.AppendLine("-".PadRight(80, '-'));
            fullResponse.AppendLine(HttpRequest.Body);
            fullResponse.AppendLine();
        }

        // Add the response section
        fullResponse.AppendLine("=".PadRight(80, '='));
        fullResponse.AppendLine("HTTP RESPONSE");
        fullResponse.AppendLine("=".PadRight(80, '='));
        fullResponse.AppendLine(HttpRequest.Response);

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

    private void ShowNotification(string title, string message, bool isSuccess)
    {
        // For now, just log to debug - could be enhanced with toast notifications
        System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {title}: {message}");
    }

    public void HandlePasswordChanged(string password)
    {
        HttpRequest.Authentication.Password = password;
    }

    public void HandleFocusedHeaderRow()
    {
        // Auto-add a new empty header row when user focuses on the last empty row
        var lastHeader = HttpRequest.Headers.LastOrDefault();
        if (lastHeader != null && !string.IsNullOrWhiteSpace(lastHeader.Key) && !string.IsNullOrWhiteSpace(lastHeader.Value))
        {
            HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
        }
    }

    public void HandleFocusedQueryParameterRow()
    {
        // Auto-add a new empty query parameter row when user focuses on the last empty row
        var lastParam = HttpRequest.QueryParameters.LastOrDefault();
        if (lastParam != null && !string.IsNullOrWhiteSpace(lastParam.Key) && !string.IsNullOrWhiteSpace(lastParam.Value))
        {
            HttpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
        }
    }

    public void Cleanup()
    {
        _currentRequestCancellation?.Cancel();
        _currentRequestCancellation?.Dispose();
        HttpRequest.PropertyChanged -= OnHttpRequestPropertyChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}