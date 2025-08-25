using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using APIHammerUI.Models;

namespace APIHammerUI
{
    public partial class HttpRequestPreviewDialog : Window
    {
        private readonly HttpRequest _httpRequest;
        private string _fullRequestText = "";

        public HttpRequestPreviewDialog(HttpRequest httpRequest)
        {
            InitializeComponent();
            _httpRequest = httpRequest;
            DataContext = this;
            
            LoadRequestPreview();
        }

        public string Method => _httpRequest.Method;
        public string FullUrl => _httpRequest.FullUrl;
        
        public SolidColorBrush MethodColor
        {
            get
            {
                return _httpRequest.Method switch
                {
                    "GET" => new SolidColorBrush(Color.FromRgb(76, 175, 80)),      // Green
                    "POST" => new SolidColorBrush(Color.FromRgb(33, 150, 243)),    // Blue
                    "PUT" => new SolidColorBrush(Color.FromRgb(255, 152, 0)),      // Orange
                    "DELETE" => new SolidColorBrush(Color.FromRgb(244, 67, 54)),   // Red
                    "PATCH" => new SolidColorBrush(Color.FromRgb(156, 39, 176)),   // Purple
                    "HEAD" => new SolidColorBrush(Color.FromRgb(96, 125, 139)),    // Blue Grey
                    "OPTIONS" => new SolidColorBrush(Color.FromRgb(121, 85, 72)), // Brown
                    _ => new SolidColorBrush(Color.FromRgb(158, 158, 158))         // Grey
                };
            }
        }

        private void LoadRequestPreview()
        {
            try
            {
                // Build headers preview
                var headersBuilder = new StringBuilder();
                var enabledHeaders = _httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)).ToList();
                
                if (enabledHeaders.Any())
                {
                    foreach (var header in enabledHeaders)
                    {
                        headersBuilder.AppendLine($"{header.Key}: {header.Value}");
                    }
                }
                else
                {
                    headersBuilder.AppendLine("No custom headers configured");
                }

                // Add authentication headers
                if (_httpRequest.Authentication.Type != AuthenticationType.None)
                {
                    switch (_httpRequest.Authentication.Type)
                    {
                        case AuthenticationType.BasicAuth:
                            if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.Username))
                            {
                                var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_httpRequest.Authentication.Username}:{_httpRequest.Authentication.Password}"));
                                headersBuilder.AppendLine($"Authorization: Basic {credentials}");
                            }
                            break;
                        case AuthenticationType.BearerToken:
                            if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.Token))
                            {
                                headersBuilder.AppendLine($"Authorization: Bearer {_httpRequest.Authentication.Token}");
                            }
                            break;
                        case AuthenticationType.ApiKey:
                            if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.ApiKeyHeader) && 
                                !string.IsNullOrWhiteSpace(_httpRequest.Authentication.ApiKeyValue))
                            {
                                headersBuilder.AppendLine($"{_httpRequest.Authentication.ApiKeyHeader}: {_httpRequest.Authentication.ApiKeyValue}");
                            }
                            break;
                    }
                }

                HeadersTextBox.Text = headersBuilder.ToString().TrimEnd();

                // Build authentication preview
                var authBuilder = new StringBuilder();
                switch (_httpRequest.Authentication.Type)
                {
                    case AuthenticationType.None:
                        authBuilder.AppendLine("Type: None");
                        authBuilder.AppendLine("No authentication configured for this request.");
                        break;
                    case AuthenticationType.BasicAuth:
                        authBuilder.AppendLine("Type: Basic Authentication");
                        authBuilder.AppendLine($"Username: {_httpRequest.Authentication.Username}");
                        authBuilder.AppendLine("Password: [Hidden for security]");
                        break;
                    case AuthenticationType.BearerToken:
                        authBuilder.AppendLine("Type: Bearer Token");
                        var tokenPreview = string.IsNullOrWhiteSpace(_httpRequest.Authentication.Token) ? 
                            "[No token configured]" : 
                            _httpRequest.Authentication.Token.Length > 50 ? 
                                _httpRequest.Authentication.Token.Substring(0, 50) + "..." : 
                                _httpRequest.Authentication.Token;
                        authBuilder.AppendLine($"Token: {tokenPreview}");
                        break;
                    case AuthenticationType.ApiKey:
                        authBuilder.AppendLine("Type: API Key");
                        authBuilder.AppendLine($"Header: {_httpRequest.Authentication.ApiKeyHeader}");
                        var keyPreview = string.IsNullOrWhiteSpace(_httpRequest.Authentication.ApiKeyValue) ? 
                            "[No key configured]" : 
                            _httpRequest.Authentication.ApiKeyValue.Length > 30 ? 
                                _httpRequest.Authentication.ApiKeyValue.Substring(0, 30) + "..." : 
                                _httpRequest.Authentication.ApiKeyValue;
                        authBuilder.AppendLine($"Value: {keyPreview}");
                        break;
                }

                AuthTextBox.Text = authBuilder.ToString().TrimEnd();

                // Build body preview
                if (!string.IsNullOrWhiteSpace(_httpRequest.Body) && 
                    (_httpRequest.Method == "POST" || _httpRequest.Method == "PUT" || _httpRequest.Method == "PATCH"))
                {
                    BodyTextBox.Text = _httpRequest.Body;
                    BodySection.Visibility = Visibility.Visible;
                }
                else
                {
                    BodyTextBox.Text = "No request body configured.";
                    if (_httpRequest.Method == "GET" || _httpRequest.Method == "HEAD" || _httpRequest.Method == "OPTIONS")
                    {
                        BodyTextBox.Text += $"\n(Note: {_httpRequest.Method} requests typically don't include a body)";
                    }
                }

                // Build full request text for clipboard
                BuildFullRequestText();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading request preview: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BuildFullRequestText()
        {
            var builder = new StringBuilder();
            
            // Request line
            builder.AppendLine($"{_httpRequest.Method} {_httpRequest.FullUrl} HTTP/1.1");
            
            // Headers
            var enabledHeaders = _httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)).ToList();
            foreach (var header in enabledHeaders)
            {
                builder.AppendLine($"{header.Key}: {header.Value}");
            }

            // Authentication headers
            if (_httpRequest.Authentication.Type != AuthenticationType.None)
            {
                switch (_httpRequest.Authentication.Type)
                {
                    case AuthenticationType.BasicAuth:
                        if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.Username))
                        {
                            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_httpRequest.Authentication.Username}:{_httpRequest.Authentication.Password}"));
                            builder.AppendLine($"Authorization: Basic {credentials}");
                        }
                        break;
                    case AuthenticationType.BearerToken:
                        if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.Token))
                        {
                            builder.AppendLine($"Authorization: Bearer {_httpRequest.Authentication.Token}");
                        }
                        break;
                    case AuthenticationType.ApiKey:
                        if (!string.IsNullOrWhiteSpace(_httpRequest.Authentication.ApiKeyHeader) && 
                            !string.IsNullOrWhiteSpace(_httpRequest.Authentication.ApiKeyValue))
                        {
                            builder.AppendLine($"{_httpRequest.Authentication.ApiKeyHeader}: {_httpRequest.Authentication.ApiKeyValue}");
                        }
                        break;
                }
            }

            // Empty line before body
            builder.AppendLine();

            // Body
            if (!string.IsNullOrWhiteSpace(_httpRequest.Body) && 
                (_httpRequest.Method == "POST" || _httpRequest.Method == "PUT" || _httpRequest.Method == "PATCH"))
            {
                builder.AppendLine(_httpRequest.Body);
            }

            _fullRequestText = builder.ToString();
        }

        private void CopyToClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_fullRequestText);
                MessageBox.Show("HTTP request copied to clipboard!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying to clipboard: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}