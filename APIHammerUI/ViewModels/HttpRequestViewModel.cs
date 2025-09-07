using APIHammerUI.Helpers;
using APIHammerUI.Models;
using APIHammerUI.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace APIHammerUI.ViewModels;

public class HttpRequestViewModel : ObservableObject, IDisposable
{
    private readonly HttpRequestService _httpRequestService;
    private CancellationTokenSource? _currentRequestCancellation;
    private bool _isDisposing = false;

    public HttpRequest HttpRequest { get; }

    public HttpRequestViewModel(HttpRequest httpRequest)
    {
        HttpRequest = httpRequest ?? throw new ArgumentNullException(nameof(httpRequest));
        _httpRequestService = new HttpRequestService();

        // Initialize commands - Use regular RelayCommand for send since it's fire-and-forget
        SendRequestCommand = new AsyncRelayCommand(ExecuteSendRequestAsync, () => !_isDisposing);
        PreviewRequestCommand = new RelayCommand(ExecutePreviewRequest, () => !_isDisposing);
        AddHeaderCommand = new RelayCommand(ExecuteAddHeader, () => !_isDisposing);
        DeleteHeaderCommand = new RelayCommand<HttpHeaderItem>(ExecuteDeleteHeader, _ => !_isDisposing);
        AddQueryParameterCommand = new RelayCommand(ExecuteAddQueryParameter, () => !_isDisposing);
        DeleteQueryParameterCommand = new RelayCommand<HttpQueryParameter>(ExecuteDeleteQueryParameter, _ => !_isDisposing);
        QuickAddHeaderCommand = new RelayCommand<string>(ExecuteQuickAddHeader, _ => !_isDisposing);
        SaveAsJsonCommand = new AsyncRelayCommand(ExecuteSaveAsJsonAsync, () => !_isDisposing && HasResponse);
        SaveAsTextCommand = new AsyncRelayCommand(ExecuteSaveAsTextAsync, () => !_isDisposing && HasResponse);
        CopyResponseCommand = new RelayCommand(ExecuteCopyResponse, () => !_isDisposing && HasResponse);
        HeaderFocusCommand = new RelayCommand(ExecuteHeaderFocus, () => !_isDisposing);
        QueryParameterFocusCommand = new RelayCommand(ExecuteQueryParameterFocus, () => !_isDisposing);
        PasswordChangedCommand = new RelayCommand<string>(ExecutePasswordChanged, _ => !_isDisposing);

        // Subscribe to property changes to update command states
        HttpRequest.PropertyChanged += OnHttpRequestPropertyChanged;
    }

    #region Commands

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
    public ICommand HeaderFocusCommand { get; }
    public ICommand QueryParameterFocusCommand { get; }
    public ICommand PasswordChangedCommand { get; }

    #endregion

    #region Properties

    public bool HasResponse => !string.IsNullOrWhiteSpace(HttpRequest.Response);

    #endregion

    #region Command Implementations

    private async Task ExecuteSendRequestAsync()
    {
        // If currently loading, cancel the request
        if (HttpRequest.IsLoading)
        {
            _currentRequestCancellation?.Cancel();
            HttpRequest.IsLoading = false;
            HttpRequest.Response = "Request cancelled by user.";
            HttpRequest.ResponseChunks.Clear();
            HttpRequest.ResponseChunks.Add("Request cancelled by user.");
            return;
        }

        _currentRequestCancellation?.Cancel();
        _currentRequestCancellation = new CancellationTokenSource();

        if (string.IsNullOrWhiteSpace(HttpRequest.Url))
        {
            HttpRequest.Response = "Please provide a URL.";
            return;
        }

        try
        {
            HttpRequest.ResponseChunks.Clear();
            HttpRequest.ResponseChunks.Add("(Connecting...)\n");
            HttpRequest.IsLoading = true;
            HttpRequest.Response = "Connecting...";
            HttpRequest.TruncatedResponse = "Connecting...";
            HttpRequest.RequestDateTime = DateTime.Now;
            HttpRequest.ResponseTime = null;
            HttpRequest.ResponseSize = null;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var displayLimitBytes = 10 * 1024; // keep same truncated display size
            long displayed = 0;

            var result = await _httpRequestService.SendRequestStreamingAsync(
                HttpRequest,
                onHeaders: headers =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        HttpRequest.ResponseChunks.Clear();
                        HttpRequest.ResponseChunks.Add(headers);
                    });
                },
                onChunk: chunk =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // maintain limited display size
                        var chunkBytes = Encoding.UTF8.GetByteCount(chunk);
                        if (displayed < displayLimitBytes)
                        {
                            HttpRequest.ResponseChunks.Add(chunk);
                            displayed += chunkBytes;
                            // trim from start if exceed
                            while (displayed > displayLimitBytes && HttpRequest.ResponseChunks.Count > 1)
                            {
                                var first = HttpRequest.ResponseChunks[1];
                                var firstBytes = Encoding.UTF8.GetByteCount(first);
                                HttpRequest.ResponseChunks.RemoveAt(1);
                                displayed -= firstBytes;
                            }
                        }
                    });
                },
                cancellationToken: _currentRequestCancellation.Token);

            sw.Stop();
            if (_currentRequestCancellation.IsCancellationRequested) return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HttpRequest.IsLoading = false;
                HttpRequest.ResponseTime = result.ResponseTime;
                HttpRequest.ResponseSize = result.ResponseSize;
                HttpRequest.Response = result.Response;
                HttpRequest.TruncatedResponse = result.Response[..Math.Min(10000, result.Response.Length)];
            });
        }
        catch (OperationCanceledException)
        {
            // already handled
        }
        catch (Exception ex)
        {
            HttpRequest.IsLoading = false;
            HttpRequest.Response = $"Error: {ex.Message}";
            HttpRequest.ResponseChunks.Add($"\nError: {ex.Message}\n");
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
            MessageBox.Show($"Error opening request preview: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteAddHeader() => HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
    private void ExecuteDeleteHeader(HttpHeaderItem? headerItem)
    {
        if (headerItem != null && HttpRequest.Headers.Count > 1)
            HttpRequest.Headers.Remove(headerItem);
    }
    private void ExecuteAddQueryParameter() => HttpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
    private void ExecuteDeleteQueryParameter(HttpQueryParameter? queryParam)
    {
        if (queryParam != null && HttpRequest.QueryParameters.Count > 1)
            HttpRequest.QueryParameters.Remove(queryParam);
    }

    private void ExecuteQuickAddHeader(string? headerName)
    {
        if (string.IsNullOrWhiteSpace(headerName)) return;
        var existingHeader = HttpRequest.Headers.FirstOrDefault(h => h.Key.Equals(headerName, StringComparison.OrdinalIgnoreCase));
        if (existingHeader != null) { existingHeader.IsEnabled = true; return; }
        var emptyHeader = HttpRequest.Headers.FirstOrDefault(h => string.IsNullOrWhiteSpace(h.Key));
        if (emptyHeader != null)
        {
            emptyHeader.Key = headerName;
            emptyHeader.Value = HttpHeaderHelper.GetDefaultValueForHeader(headerName);
            emptyHeader.IsEnabled = true;
        }
        else
        {
            var newHeader = new HttpHeaderItem { Key = headerName, Value = HttpHeaderHelper.GetDefaultValueForHeader(headerName), IsEnabled = true };
            HttpRequest.Headers.Add(newHeader);
            HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "" });
        }
    }

    private async Task ExecuteSaveAsJsonAsync()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;
        try
        {
            var jsonContent = ExtractResponseBody(HttpRequest.Response);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                MessageBox.Show("No response body found to save.", "Save JSON", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string formattedJson;
            bool isValidJson = true;
            try
            {
                using var document = JsonDocument.Parse(jsonContent);
                formattedJson = JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (JsonException)
            {
                isValidJson = false;
                var result = MessageBox.Show("The response body doesn't appear to be valid JSON. Save as plain text instead?", "Invalid JSON", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;
                formattedJson = jsonContent;
            }
            var extension = isValidJson ? ".json" : ".txt";
            var filter = isValidJson ? "JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt|All Files (*.*)|*.*" : "Text Files (*.txt)|*.txt|JSON Files (*.json)|*.json|All Files (*.*)|*.*";
            var saveDialog = new SaveFileDialog { Title = "Save Response Body", Filter = filter, DefaultExt = extension, FileName = GenerateFileName("response_body", extension) };
            if (saveDialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(saveDialog.FileName, formattedJson, Encoding.UTF8);
                MessageBox.Show($"Response body saved successfully!\n\nFile: {saveDialog.FileName}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExecuteSaveAsTextAsync()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;
        try
        {
            var saveDialog = new SaveFileDialog { Title = "Save Full Response as Text", Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|Markdown Files (*.md)|*.md|All Files (*.*)|*.*", DefaultExt = ".txt", FileName = GenerateFileName("http_response", ".txt") };
            if (saveDialog.ShowDialog() == true)
            {
                var fullResponse = BuildFullResponseForSave();
                await File.WriteAllTextAsync(saveDialog.FileName, fullResponse, Encoding.UTF8);
                MessageBox.Show($"Full response saved successfully!\n\nFile: {saveDialog.FileName}", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExecuteCopyResponse()
    {
        if (string.IsNullOrWhiteSpace(HttpRequest.Response)) return;
        try { Clipboard.SetText(HttpRequest.Response); MessageBox.Show("Response copied to clipboard!", "Copy Complete", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Copy Error", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ExecuteHeaderFocus()
    {
        var lastHeader = HttpRequest.Headers.LastOrDefault();
        if (lastHeader != null && !string.IsNullOrWhiteSpace(lastHeader.Key) && !string.IsNullOrWhiteSpace(lastHeader.Value))
            HttpRequest.Headers.Add(new HttpHeaderItem { Key = "", Value = "", IsEnabled = true });
    }

    private void ExecuteQueryParameterFocus()
    {
        var lastParam = HttpRequest.QueryParameters.LastOrDefault();
        if (lastParam != null && !string.IsNullOrWhiteSpace(lastParam.Key) && !string.IsNullOrWhiteSpace(lastParam.Value))
            HttpRequest.QueryParameters.Add(new HttpQueryParameter { Key = "", Value = "", IsEnabled = true });
    }

    private void ExecutePasswordChanged(string? password)
    {
        if (password != null)
            HttpRequest.Authentication.Password = password;
    }

    #endregion // Command Implementations

    // Helper methods

    private string ExtractResponseBody(string fullResponse)
    {
        const string bodyMarker = "Response Body:";
        var bodyIndex = fullResponse.IndexOf(bodyMarker, StringComparison.OrdinalIgnoreCase);
        if (bodyIndex == -1) return string.Empty;
        var startIndex = bodyIndex + bodyMarker.Length;
        var responseBody = fullResponse.Substring(startIndex).Trim();
        return responseBody;
    }

    private string BuildFullResponseForSave()
    {
        var sb = new StringBuilder();
        sb.AppendLine(new string('=', 80));
        sb.AppendLine("API HAMMER - HTTP Response Export");
        sb.AppendLine(new string('=', 80));
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("REQUEST INFORMATION");
        sb.AppendLine($"Method: {HttpRequest.Method}");
        sb.AppendLine($"URL: {HttpRequest.FullUrl}");
        if (HttpRequest.RequestDateTime.HasValue) sb.AppendLine($"Request Sent: {HttpRequest.RequestDateTime:yyyy-MM-dd HH:mm:ss.fff}");
        if (HttpRequest.ResponseTime.HasValue) sb.AppendLine($"Response Time: {HttpRequest.ResponseTimeFormatted}");
        if (HttpRequest.ResponseSize.HasValue) sb.AppendLine($"Response Size: {HttpRequest.ResponseSizeFormatted}");
        sb.AppendLine();
        sb.AppendLine(HttpRequest.Response);
        return sb.ToString();
    }

    private string GenerateFileName(string prefix, string extension) => $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";

    private void OnHttpRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HttpRequest.Response) || e.PropertyName == nameof(HttpRequest.TruncatedResponse))
            OnPropertyChanged(nameof(HasResponse));
    }

    public void Dispose()
    {
        _isDisposing = true;
        _currentRequestCancellation?.Cancel();
        _currentRequestCancellation?.Dispose();
        HttpRequest.PropertyChanged -= OnHttpRequestPropertyChanged;
    }
}