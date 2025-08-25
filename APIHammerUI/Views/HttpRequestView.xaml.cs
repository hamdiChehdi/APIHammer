using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;
using Newtonsoft.Json;

namespace APIHammerUI.Views
{
    public partial class HttpRequestView : UserControl
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public HttpRequestView()
        {
            InitializeComponent();
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

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not HttpRequest httpRequest)
                return;

            try
            {
                httpRequest.IsLoading = true;
                httpRequest.Response = "Loading...";
                
                // Reset response metadata
                httpRequest.ResponseTime = null;
                httpRequest.ResponseSize = null;
                httpRequest.RequestDateTime = DateTime.Now;

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var request = new HttpRequestMessage();
                
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
                if (Uri.TryCreate(httpRequest.FullUrl, UriKind.Absolute, out var uri))
                {
                    request.RequestUri = uri;
                }
                else
                {
                    httpRequest.Response = "Invalid URL format. Please check your base URL and query parameters.";
                    return;
                }

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

                var response = await httpClient.SendAsync(request);
                stopwatch.Stop();
                
                // Capture response metadata
                httpRequest.ResponseTime = stopwatch.Elapsed;
                
                var responseContent = await response.Content.ReadAsStringAsync();
                httpRequest.ResponseSize = Encoding.UTF8.GetByteCount(responseContent);

                // Format response
                var responseText = new StringBuilder();
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

                // Try to format JSON
                try
                {
                    if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
                    {
                        var formatted = JsonConvert.SerializeObject(JsonConvert.DeserializeObject(responseContent), Formatting.Indented);
                        responseText.AppendLine(formatted);
                    }
                    else
                    {
                        responseText.AppendLine(responseContent);
                    }
                }
                catch
                {
                    responseText.AppendLine(responseContent);
                }

                httpRequest.Response = responseText.ToString();
            }
            catch (Exception ex)
            {
                httpRequest.Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}\n\nStack Trace:\n{ex.StackTrace}";
            }
            finally
            {
                httpRequest.IsLoading = false;
            }
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