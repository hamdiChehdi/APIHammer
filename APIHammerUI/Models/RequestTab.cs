using System;
using APIHammerUI.Helpers;
using System.ComponentModel;

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
            _requestType = value;
            OnPropertyChanged(nameof(RequestType));
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

    public object? Content { get; set; }
}