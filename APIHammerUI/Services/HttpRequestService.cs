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

    // Default cap (will be overridden per request by advanced settings)
    private const long DEFAULT_MAX_CAPTURE_BYTES = 100L * 1024 * 1024; // 100MB safeguard to avoid OOM

    static HttpRequestService()
    {
        if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            httpClient.DefaultRequestHeaders.Add("User-Agent", "APIHammer/1.0");
    }

    /// <summary>
    /// Sends an HTTP request and returns the result (no artificial response size limit).
    /// NOTE: Non-streaming path; still respects advanced formatting toggle.
    /// </summary>
    public async Task<HttpRequestResult> SendRequestAsync(HttpRequest httpRequest, CancellationToken cancellationToken = default)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;
        using var cts = CreateLinkedTimeoutCts(httpRequest.TimeoutSeconds, cancellationToken);
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            request = BuildRequest(httpRequest);
            if (request == null)
            {
                return new HttpRequestResult { Success = false, ErrorMessage = "Invalid URL format.", RequestDateTime = DateTime.Now };
            }
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            stopwatch.Stop();
            var headers = BuildResponseHeaders(response, request, httpRequest);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var formatted = httpRequest.FormatJson ? await FormatResponseContentAsync(body, response).ConfigureAwait(false) : body;
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
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = "Request timed out", Response = "Request timed out.", RequestDateTime = DateTime.Now };
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
    /// Streaming request that invokes callbacks for header and content chunks. Avoids keeping the entire body in memory.
    /// Respects advanced settings for timeout, capture size, and formatting.
    /// </summary>
    public async Task<HttpRequestResult> SendRequestStreamingAsync(
        HttpRequest httpRequest,
        Action<string>? onHeaders,
        Action<string>? onChunk,
        CancellationToken cancellationToken = default)
    {
        HttpRequestMessage? request = null;
        HttpResponseMessage? response = null;

        // Determine capture limit (convert MB to bytes, clamp minimum 1MB, maximum maybe 1GB for safety)
        var maxCaptureBytes = DEFAULT_MAX_CAPTURE_BYTES;
        if (httpRequest.MaxCaptureSizeMB > 0)
        {
            maxCaptureBytes = Math.Clamp((long)httpRequest.MaxCaptureSizeMB * 1024 * 1024, 1L * 1024 * 1024, 1024L * 1024 * 1024);
        }

        var capturedBuilder = new StringBuilder(64 * 1024); // start modest
        long capturedBytes = 0;
        long totalBytes = 0;
        bool truncated = false;
        using var cts = CreateLinkedTimeoutCts(httpRequest.TimeoutSeconds, cancellationToken);
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            request = BuildRequest(httpRequest);
            if (request == null)
            {
                return new HttpRequestResult { Success = false, ErrorMessage = "Invalid URL format." };
            }
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var headerText = BuildResponseHeaders(response, request, httpRequest);
            onHeaders?.Invoke(headerText);

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);
            var buffer = new char[16 * 1024];
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token)) > 0)
            {
                var slice = new string(buffer, 0, read);
                var sliceByteCount = Encoding.UTF8.GetByteCount(slice);
                totalBytes += sliceByteCount;

                // Forward slice to UI (display layer already truncates)
                onChunk?.Invoke(slice);

                // Capture only up to maxCaptureBytes
                if (!truncated)
                {
                    if (capturedBytes + sliceByteCount <= maxCaptureBytes)
                    {
                        capturedBuilder.Append(slice);
                        capturedBytes += sliceByteCount;
                    }
                    else
                    {
                        var remaining = (int)(maxCaptureBytes - capturedBytes);
                        if (remaining > 0)
                        {
                            capturedBuilder.Append(slice); // small overshoot acceptable
                        }
                        truncated = true;
                    }
                }

                if (truncated)
                {
                    break; // stop downloading more if we decided to truncate
                }
            }
            stopwatch.Stop();

            string bodyPortion = capturedBuilder.ToString();
            if (!truncated && httpRequest.FormatJson)
            {
                bodyPortion = await FormatResponseContentAsync(bodyPortion, response).ConfigureAwait(false);
            }
            else if (truncated)
            {
                bodyPortion += $"\n[Body truncated after {capturedBytes / (1024.0 * 1024.0):F1} MB (limit {maxCaptureBytes / (1024.0 * 1024.0):F1} MB). Full content not downloaded.]";
            }

            var fullResponse = headerText + bodyPortion;
            return new HttpRequestResult
            {
                Success = true,
                Response = fullResponse,
                ResponseTime = stopwatch.Elapsed,
                ResponseSize = truncated ? capturedBytes : totalBytes,
                RequestDateTime = DateTime.Now
            };
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return new HttpRequestResult { Success = false, ErrorMessage = "Request timed out", Response = "Request timed out." };
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

    private static CancellationTokenSource CreateLinkedTimeoutCts(int? timeoutSeconds, CancellationToken externalToken)
    {
        if (timeoutSeconds.HasValue && timeoutSeconds.Value > 0)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds.Value));
            return cts;
        }
        // no timeout -> link only
        return CancellationTokenSource.CreateLinkedTokenSource(externalToken);
    }

    private static HttpRequestMessage? BuildRequest(HttpRequest httpRequest)
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

    private string BuildResponseHeaders(HttpResponseMessage response, HttpRequestMessage request, HttpRequest httpRequest)
    {
        var responseText = new StringBuilder(512);
        responseText.AppendLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        responseText.AppendLine($"Request URL: {request.RequestUri}");
        if (httpRequest.Authentication.Type != AuthenticationType.None)
        {
            responseText.AppendLine($"Authentication: {httpRequest.Authentication.Type}");
        }
        if (httpRequest.TimeoutSeconds.HasValue && httpRequest.TimeoutSeconds.Value > 0)
        {
            responseText.AppendLine($"Timeout: {httpRequest.TimeoutSeconds.Value}s");
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

    private async Task<string> FormatResponseContentAsync(string content, HttpResponseMessage response)
    {
        const int largeResponseThreshold = 1024 * 1024; // 1MB
        if (content.Length > largeResponseThreshold)
            return content;
        try
        {
            if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
            {
                using var document = JsonDocument.Parse(content);
                var options = new JsonSerializerOptions { WriteIndented = true };
                return JsonSerializer.Serialize(document, options);
            }
        }
        catch (JsonException)
        {
        }
        return content;
    }
}