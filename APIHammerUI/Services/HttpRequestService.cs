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
    // Unlimited timeout for very large / long running responses. User still can cancel via cancellation token.
    private static readonly HttpClient httpClient = new HttpClient()
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan
    };

    static HttpRequestService()
    {
        if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            httpClient.DefaultRequestHeaders.Add("User-Agent", "APIHammer/1.0");
    }

    /// <summary>
    /// Sends an HTTP request and returns the result (no artificial response size limit).
    /// </summary>
    public async Task<HttpRequestResult> SendRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            request = BuildRequest(httpRequest);
            if (request == null)
            {
                return new HttpRequestResult { Success = false, ErrorMessage = "Invalid URL format.", RequestDateTime = DateTime.Now };
            }
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            var headers = BuildResponseHeaders(response, request, httpRequest);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var formatted = await FormatResponseContentAsync(body, response).ConfigureAwait(false);
            var full = headers + formatted;
            return new HttpRequestResult
            {
                Success = true,
                Response = full,
                ResponseTime = stopwatch.Elapsed,
                ResponseSize = Encoding.UTF8.GetByteCount(body),
                RequestDateTime = DateTime.Now
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = "Request was cancelled", Response = "Request was cancelled.", RequestDateTime = DateTime.Now };
        }
        catch (Exception ex)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = ex.Message, Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}", RequestDateTime = DateTime.Now };
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    /// <summary>
    /// Streaming request that invokes callbacks for header and content chunks.
    /// </summary>
    public async Task<HttpRequestResult> SendRequestStreamingAsync(
        HttpRequest httpRequest,
        Action<string>? onHeaders,
        Action<string>? onChunk,
        CancellationToken cancellationToken = default)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        var fullBodyBuilder = new StringBuilder();
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            request = BuildRequest(httpRequest);
            if (request == null)
            {
                return new HttpRequestResult { Success = false, ErrorMessage = "Invalid URL format." };
            }
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var headerText = BuildResponseHeaders(response, request, httpRequest);
            onHeaders?.Invoke(headerText);

            // Stream body
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);
            var buffer = new char[16 * 1024];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                var slice = new string(buffer, 0, read);
                fullBodyBuilder.Append(slice);
                onChunk?.Invoke(slice);
            }
            stopwatch.Stop();
            var fullBody = fullBodyBuilder.ToString();
            var maybeFormatted = await FormatResponseContentAsync(fullBody, response).ConfigureAwait(false);
            var fullResponse = headerText + maybeFormatted;
            return new HttpRequestResult
            {
                Success = true,
                Response = fullResponse,
                ResponseTime = stopwatch.Elapsed,
                ResponseSize = Encoding.UTF8.GetByteCount(fullBody),
                RequestDateTime = DateTime.Now
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = "Request was cancelled", Response = "Request was cancelled." };
        }
        catch (Exception ex)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = ex.Message, Response = $"Error: {ex.Message}\n\nRequest URL: {httpRequest.FullUrl}" };
        }
        finally
        {
            response?.Dispose();
            request?.Dispose();
        }
    }

    private HttpRequestMessage? BuildRequest(HttpRequest httpRequest)
    {
        if (!Uri.TryCreate(httpRequest.FullUrl, UriKind.Absolute, out var uri))
            return null;
        var request = new HttpRequestMessage
        {
            Method = httpRequest.Method switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => HttpMethod.Patch,
                "HEAD" => HttpMethod.Head,
                "OPTIONS" => HttpMethod.Options,
                _ => HttpMethod.Get
            },
            RequestUri = uri
        };
        ApplyAuthentication(request, httpRequest.Authentication);
        foreach (var headerItem in httpRequest.Headers.Where(h => h.IsEnabled && !string.IsNullOrWhiteSpace(h.Key)))
        {
            // Skip Authorization header if it's already set by authentication
            if (headerItem.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) && 
                (httpRequest.Authentication.Type == AuthenticationType.BasicAuth || 
                 httpRequest.Authentication.Type == AuthenticationType.BearerToken))
                continue;

            // Try to add to request headers first
            request.Headers.TryAddWithoutValidation(headerItem.Key, headerItem.Value);
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
        }

        return request;
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
        var responseText = new StringBuilder(512); // Reduce initial capacity to save memory
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
        catch (JsonException)
        {
            // Return original content if formatting fails
        }

        return content;
    }
}