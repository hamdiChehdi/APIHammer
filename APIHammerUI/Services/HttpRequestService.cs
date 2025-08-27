using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APIHammerUI.Models;
using System.Text.Json;

namespace APIHammerUI.Services;

public class HttpRequestResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public long ResponseSize { get; set; }
    public DateTime RequestDateTime { get; set; }
    public string Response { get; set; } = "";
}

public class HttpRequestService
{
    private static readonly HttpClient httpClient = new HttpClient()
    {
        MaxResponseContentBufferSize = 10 * 1024 * 1024, // 10MB limit
        Timeout = TimeSpan.FromMinutes(5) // 5 minute timeout
    };

    static HttpRequestService()
    {
        // Configure HttpClient for better performance
        httpClient.DefaultRequestHeaders.Add("User-Agent", "APIHammer/1.0");
    }

    /// <summary>
    /// Sends an HTTP request and returns the result
    /// </summary>
    public async Task<HttpRequestResult> SendRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        
        try
        {
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
                return new HttpRequestResult
                {
                    Success = false,
                    ErrorMessage = "Invalid URL format. Please check your base URL and query parameters.",
                    RequestDateTime = DateTime.Now
                };
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

            // Send the request
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            stopwatch.Stop();

            // Build response headers
            var responseHeaders = BuildResponseHeaders(response, request, httpRequest);

            // Check response size first
            var contentLength = response.Content.Headers.ContentLength;
            const int maxResponseSize = 10 * 1024 * 1024; // 10MB
            
            if (contentLength.HasValue && contentLength.Value > maxResponseSize)
            {
                return new HttpRequestResult
                {
                    Success = false,
                    ErrorMessage = "Response too large",
                    Response = responseHeaders + 
                        $"\nResponse too large ({contentLength.Value / (1024 * 1024):F1} MB). " +
                        "Maximum supported size is 10 MB.",
                    ResponseTime = stopwatch.Elapsed,
                    ResponseSize = contentLength.Value,
                    RequestDateTime = DateTime.Now
                };
            }

            // Read response content
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var formattedContent = await FormatResponseContentAsync(responseContent, response);

            var fullResponse = responseHeaders + formattedContent;

            return new HttpRequestResult
            {
                Success = true,
                Response = fullResponse,
                ResponseTime = stopwatch.Elapsed,
                ResponseSize = Encoding.UTF8.GetByteCount(responseContent),
                RequestDateTime = DateTime.Now
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HttpRequestResult
            {
                Success = false,
                ErrorMessage = "Request was cancelled",
                Response = "Request was cancelled.",
                RequestDateTime = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            return new HttpRequestResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}",
                RequestDateTime = DateTime.Now
            };
        }
        finally
        {
            // Dispose resources
            response?.Dispose();
            request?.Dispose();
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

    private async Task<string> FormatResponseContentAsync(string content, HttpResponseMessage response)
    {
        const int largeResponseThreshold = 1024 * 1024; // 1MB
        
        // Don't format very large responses to avoid memory issues
        if (content.Length > largeResponseThreshold)
        {
            return content;
        }

        return await Task.Run(() =>
        {
            try
            {
                if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
                {
                    // Use System.Text.Json for formatting
                    using var document = JsonDocument.Parse(content);
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    return JsonSerializer.Serialize(document, options);
                }
            }
            catch
            {
                // Return original content if formatting fails
            }

            return content;
        });
    }
}