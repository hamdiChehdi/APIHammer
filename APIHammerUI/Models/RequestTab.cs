using System;
using APIHammerUI.Helpers;
using System.ComponentModel;
using APIHammerUI.Views; // for HttpRequestView

namespace APIHammerUI.Models;

public enum RequestType
{
    HTTP,
    WebSocket,
    gRPC
}

public class RequestTab : ObservableObject
{
    private string _name = "New Request";
    private RequestType _requestType = RequestType.HTTP;
    private bool _isSelected;
    private object? _content;
    private INotifyPropertyChanged? _httpRequestNotifier; // track subscription

    public RequestTab()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Unique identifier for persistence
    /// </summary>
    public Guid Id { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged(nameof(Name));
        }
    }

    public RequestType RequestType
    {
        get => _requestType;
        set
        {
            if (_requestType == value) return;
            _requestType = value;
            OnPropertyChanged(nameof(RequestType));
            OnPropertyChanged(nameof(DisplayMethodLabel));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public object? Content
    {
        get => _content;
        set
        {
            if (ReferenceEquals(_content, value)) return;
            UnsubscribeFromHttpMethod();
            _content = value;
            SubscribeToHttpMethod();
            OnPropertyChanged(nameof(Content));
            OnPropertyChanged(nameof(DisplayMethodLabel));
        }
    }

    /// <summary>
    /// Label shown in tree (updates when HTTP method changes)
    /// </summary>
    public string DisplayMethodLabel
    {
        get
        {
            if (RequestType == RequestType.HTTP && Content is HttpRequestView hv && hv.ViewModel?.HttpRequest != null)
            {
                var method = hv.ViewModel.HttpRequest.Method;
                return string.IsNullOrWhiteSpace(method) ? "HTTP" : $"HTTP {method}";
            }
            return RequestType switch
            {
                RequestType.WebSocket => "WS",
                RequestType.gRPC => "gRPC",
                RequestType.HTTP => "HTTP",
                _ => "REQ"
            };
        }
    }

    private void SubscribeToHttpMethod()
    {
        if (Content is HttpRequestView hv && hv.ViewModel?.HttpRequest is INotifyPropertyChanged npc)
        {
            _httpRequestNotifier = npc;
            npc.PropertyChanged += HttpRequest_PropertyChanged;
        }
    }

    private void UnsubscribeFromHttpMethod()
    {
        if (_httpRequestNotifier != null)
        {
            _httpRequestNotifier.PropertyChanged -= HttpRequest_PropertyChanged;
            _httpRequestNotifier = null;
        }
    }

    private void HttpRequest_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Method")
        {
            OnPropertyChanged(nameof(DisplayMethodLabel));
        }
    }
}