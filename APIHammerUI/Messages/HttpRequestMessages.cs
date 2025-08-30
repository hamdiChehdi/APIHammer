using System;
using System.Threading;
using APIHammerUI.Models;

namespace APIHammerUI.Messages;

/// <summary>
/// Message to request HTTP request processing
/// </summary>
public class HttpRequestMessage : IMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// The HTTP request to process
    /// </summary>
    public HttpRequest Request { get; set; } = null!;
    
    /// <summary>
    /// Cancellation token for the request
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = default;
    
    /// <summary>
    /// Callback to invoke when request processing is complete
    /// </summary>
    public Action<HttpRequestResponseMessage>? CompletionCallback { get; set; }
}

/// <summary>
/// Message containing the result of HTTP request processing
/// </summary>
public class HttpRequestResponseMessage : IMessage
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    public int Priority { get; set; } = 0;
    
    /// <summary>
    /// ID of the original request message
    /// </summary>
    public Guid OriginalRequestId { get; set; }
    
    /// <summary>
    /// The HTTP request that was processed
    /// </summary>
    public HttpRequest Request { get; set; } = null!;
    
    /// <summary>
    /// Whether the request was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if the request failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Response time for the request
    /// </summary>
    public TimeSpan ResponseTime { get; set; }
    
    /// <summary>
    /// Size of the response in bytes
    /// </summary>
    public long ResponseSize { get; set; }
    
    /// <summary>
    /// When the request was sent
    /// </summary>
    public DateTime RequestDateTime { get; set; }
    
    /// <summary>
    /// The response content
    /// </summary>
    public string Response { get; set; } = "";
}