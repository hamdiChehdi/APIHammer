using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using APIHammerUI.Models;
using APIHammerUI.Models.Persistence;
using APIHammerUI.Views;

namespace APIHammerUI.Services;

/// <summary>
/// Service for mapping between UI models and persistent models
/// </summary>
public static class ModelMappingService
{
    #region TabCollection Mapping

    /// <summary>
    /// Convert TabCollection to PersistentTabCollection
    /// </summary>
    public static PersistentTabCollection ToPersistent(TabCollection collection, Guid? id = null)
    {
        return new PersistentTabCollection
        {
            Id = id ?? collection.Id,
            Name = collection.Name,
            TabIds = collection.Tabs.Select(tab => tab.Id).ToList(),
            Order = 0 // Will be set by the calling code
        };
    }

    /// <summary>
    /// Convert PersistentTabCollection to TabCollection
    /// </summary>
    public static TabCollection FromPersistent(PersistentTabCollection persistent)
    {
        return new TabCollection
        {
            Id = persistent.Id,
            Name = persistent.Name
        };
    }

    #endregion

    #region RequestTab Mapping

    /// <summary>
    /// Convert RequestTab to PersistentRequestTab
    /// </summary>
    public static PersistentRequestTab ToPersistent(RequestTab tab, Guid collectionId, Guid? id = null)
    {
        var persistent = new PersistentRequestTab
        {
            Id = id ?? tab.Id,
            Name = tab.Name,
            RequestType = tab.RequestType,
            CollectionId = collectionId,
            Order = 0 // Will be set by the calling code
        };

        // Map the request content based on type
        switch (tab.RequestType)
        {
            case RequestType.HTTP:
                if (tab.Content is HttpRequestView httpView && httpView.DataContext is HttpRequest httpRequest)
                {
                    persistent.HttpRequest = ToPersistent(httpRequest);
                }
                else if (tab.Content is HttpRequest directHttpRequest)
                {
                    persistent.HttpRequest = ToPersistent(directHttpRequest);
                }
                break;

            case RequestType.WebSocket:
                if (tab.Content is WebSocketRequestView wsView && wsView.DataContext is WebSocketRequest wsRequest)
                {
                    persistent.WebSocketRequest = ToPersistent(wsRequest);
                }
                else if (tab.Content is WebSocketRequest directWsRequest)
                {
                    persistent.WebSocketRequest = ToPersistent(directWsRequest);
                }
                break;

            case RequestType.gRPC:
                if (tab.Content is GrpcRequestView grpcView && grpcView.DataContext is GrpcRequest grpcRequest)
                {
                    persistent.GrpcRequest = ToPersistent(grpcRequest);
                }
                else if (tab.Content is GrpcRequest directGrpcRequest)
                {
                    persistent.GrpcRequest = ToPersistent(directGrpcRequest);
                }
                break;
        }

        return persistent;
    }

    /// <summary>
    /// Convert PersistentRequestTab to RequestTab
    /// </summary>
    public static RequestTab FromPersistent(PersistentRequestTab persistent)
    {
        var tab = new RequestTab
        {
            Id = persistent.Id,
            Name = persistent.Name,
            RequestType = persistent.RequestType
        };

        // Create the appropriate content based on request type
        switch (persistent.RequestType)
        {
            case RequestType.HTTP:
                var httpRequest = persistent.HttpRequest != null 
                    ? FromPersistent(persistent.HttpRequest) 
                    : new HttpRequest();
                var httpView = new HttpRequestView { DataContext = httpRequest };
                tab.Content = httpView;
                break;

            case RequestType.WebSocket:
                var wsRequest = persistent.WebSocketRequest != null 
                    ? FromPersistent(persistent.WebSocketRequest) 
                    : new WebSocketRequest();
                var wsView = new WebSocketRequestView { DataContext = wsRequest };
                tab.Content = wsView;
                break;

            case RequestType.gRPC:
                var grpcRequest = persistent.GrpcRequest != null 
                    ? FromPersistent(persistent.GrpcRequest) 
                    : new GrpcRequest();
                var grpcView = new GrpcRequestView { DataContext = grpcRequest };
                tab.Content = grpcView;
                break;
        }

        return tab;
    }

    #endregion

    #region HttpRequest Mapping

    /// <summary>
    /// Convert HttpRequest to PersistentHttpRequest
    /// </summary>
    public static PersistentHttpRequest ToPersistent(HttpRequest httpRequest)
    {
        return new PersistentHttpRequest
        {
            Method = httpRequest.Method,
            Url = httpRequest.Url,
            Body = httpRequest.Body,
            Headers = httpRequest.Headers.Select(ToPersistent).ToList(),
            QueryParameters = httpRequest.QueryParameters.Select(ToPersistent).ToList(),
            Authentication = ToPersistent(httpRequest.Authentication)
        };
    }

    /// <summary>
    /// Convert PersistentHttpRequest to HttpRequest
    /// </summary>
    public static HttpRequest FromPersistent(PersistentHttpRequest persistent)
    {
        var httpRequest = new HttpRequest
        {
            Method = persistent.Method,
            Url = persistent.Url,
            Body = persistent.Body,
            Authentication = FromPersistent(persistent.Authentication)
        };

        // Clear default collections and add persistent data
        httpRequest.Headers.Clear();
        foreach (var header in persistent.Headers)
        {
            httpRequest.Headers.Add(FromPersistent(header));
        }

        httpRequest.QueryParameters.Clear();
        foreach (var param in persistent.QueryParameters)
        {
            httpRequest.QueryParameters.Add(FromPersistent(param));
        }

        return httpRequest;
    }

    #endregion

    #region HttpHeaderItem Mapping

    /// <summary>
    /// Convert HttpHeaderItem to PersistentHttpHeader
    /// </summary>
    public static PersistentHttpHeader ToPersistent(HttpHeaderItem header)
    {
        return new PersistentHttpHeader
        {
            Key = header.Key,
            Value = header.Value,
            IsEnabled = header.IsEnabled
        };
    }

    /// <summary>
    /// Convert PersistentHttpHeader to HttpHeaderItem
    /// </summary>
    public static HttpHeaderItem FromPersistent(PersistentHttpHeader persistent)
    {
        return new HttpHeaderItem
        {
            Key = persistent.Key,
            Value = persistent.Value,
            IsEnabled = persistent.IsEnabled
        };
    }

    #endregion

    #region HttpQueryParameter Mapping

    /// <summary>
    /// Convert HttpQueryParameter to PersistentQueryParameter
    /// </summary>
    public static PersistentQueryParameter ToPersistent(HttpQueryParameter param)
    {
        return new PersistentQueryParameter
        {
            Key = param.Key,
            Value = param.Value,
            IsEnabled = param.IsEnabled
        };
    }

    /// <summary>
    /// Convert PersistentQueryParameter to HttpQueryParameter
    /// </summary>
    public static HttpQueryParameter FromPersistent(PersistentQueryParameter persistent)
    {
        return new HttpQueryParameter
        {
            Key = persistent.Key,
            Value = persistent.Value,
            IsEnabled = persistent.IsEnabled
        };
    }

    #endregion

    #region AuthenticationSettings Mapping

    /// <summary>
    /// Convert AuthenticationSettings to PersistentAuthenticationSettings
    /// </summary>
    public static PersistentAuthenticationSettings ToPersistent(AuthenticationSettings auth)
    {
        return new PersistentAuthenticationSettings
        {
            Type = auth.Type,
            Username = auth.Username,
            Password = auth.Password,
            Token = auth.Token,
            ApiKeyHeader = auth.ApiKeyHeader,
            ApiKeyValue = auth.ApiKeyValue
        };
    }

    /// <summary>
    /// Convert PersistentAuthenticationSettings to AuthenticationSettings
    /// </summary>
    public static AuthenticationSettings FromPersistent(PersistentAuthenticationSettings persistent)
    {
        return new AuthenticationSettings
        {
            Type = persistent.Type,
            Username = persistent.Username,
            Password = persistent.Password,
            Token = persistent.Token,
            ApiKeyHeader = persistent.ApiKeyHeader,
            ApiKeyValue = persistent.ApiKeyValue
        };
    }

    #endregion

    #region WebSocketRequest Mapping

    /// <summary>
    /// Convert WebSocketRequest to PersistentWebSocketRequest
    /// </summary>
    public static PersistentWebSocketRequest ToPersistent(WebSocketRequest wsRequest)
    {
        return new PersistentWebSocketRequest
        {
            Url = wsRequest.Url,
            Headers = wsRequest.Headers.Select(h => new PersistentHttpHeader 
            { 
                Key = h.Key, 
                Value = h.Value, 
                IsEnabled = h.IsEnabled 
            }).ToList()
        };
    }

    /// <summary>
    /// Convert PersistentWebSocketRequest to WebSocketRequest
    /// </summary>
    public static WebSocketRequest FromPersistent(PersistentWebSocketRequest persistent)
    {
        var wsRequest = new WebSocketRequest
        {
            Url = persistent.Url
        };

        wsRequest.Headers.Clear();
        foreach (var header in persistent.Headers)
        {
            wsRequest.Headers.Add(new HttpHeaderItem
            {
                Key = header.Key,
                Value = header.Value,
                IsEnabled = header.IsEnabled
            });
        }

        return wsRequest;
    }

    #endregion

    #region GrpcRequest Mapping

    /// <summary>
    /// Convert GrpcRequest to PersistentGrpcRequest
    /// </summary>
    public static PersistentGrpcRequest ToPersistent(GrpcRequest grpcRequest)
    {
        return new PersistentGrpcRequest
        {
            Server = grpcRequest.Server,
            Service = grpcRequest.Service,
            Method = grpcRequest.Method,
            Request = grpcRequest.Request
        };
    }

    /// <summary>
    /// Convert PersistentGrpcRequest to GrpcRequest
    /// </summary>
    public static GrpcRequest FromPersistent(PersistentGrpcRequest persistent)
    {
        return new GrpcRequest
        {
            Server = persistent.Server,
            Service = persistent.Service,
            Method = persistent.Method,
            Request = persistent.Request
        };
    }

    #endregion
}