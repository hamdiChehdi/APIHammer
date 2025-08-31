using System;
using System.Collections.Generic;
using LiteDB;

namespace APIHammerUI.Models.Persistence;

/// <summary>
/// Persistent version of TabCollection for database storage
/// </summary>
public class PersistentTabCollection
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Name { get; set; } = "New Collection";
    
    public List<Guid> TabIds { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    public int Order { get; set; } = 0;
}

/// <summary>
/// Persistent version of RequestTab for database storage
/// </summary>
public class PersistentRequestTab
{
    [BsonId]
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public string Name { get; set; } = "New Request";
    
    public RequestType RequestType { get; set; } = RequestType.HTTP;
    
    public Guid CollectionId { get; set; }
    
    public int Order { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // Request content based on type
    public PersistentHttpRequest? HttpRequest { get; set; }
    public PersistentWebSocketRequest? WebSocketRequest { get; set; }
    public PersistentGrpcRequest? GrpcRequest { get; set; }
}

/// <summary>
/// Persistent version of HttpRequest for database storage
/// </summary>
public class PersistentHttpRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public string Body { get; set; } = "";
    public List<PersistentHttpHeader> Headers { get; set; } = new();
    public List<PersistentQueryParameter> QueryParameters { get; set; } = new();
    public PersistentAuthenticationSettings Authentication { get; set; } = new();
}

/// <summary>
/// Persistent version of HttpHeaderItem for database storage
/// </summary>
public class PersistentHttpHeader
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Persistent version of HttpQueryParameter for database storage
/// </summary>
public class PersistentQueryParameter
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Persistent version of AuthenticationSettings for database storage
/// </summary>
public class PersistentAuthenticationSettings
{
    public AuthenticationType Type { get; set; } = AuthenticationType.None;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Token { get; set; } = "";
    public string ApiKeyHeader { get; set; } = "X-API-Key";
    public string ApiKeyValue { get; set; } = "";
}

/// <summary>
/// Persistent version of WebSocketRequest for database storage
/// </summary>
public class PersistentWebSocketRequest
{
    public string Url { get; set; } = "";
    public List<PersistentHttpHeader> Headers { get; set; } = new();
}

/// <summary>
/// Persistent version of GrpcRequest for database storage
/// </summary>
public class PersistentGrpcRequest
{
    public string Server { get; set; } = "";
    public string Service { get; set; } = "";
    public string Method { get; set; } = "";
    public string Request { get; set; } = "";
}