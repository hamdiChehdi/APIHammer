using System.ComponentModel;

namespace APIHammerUI.Models;

public enum RequestType
{
    HTTP,
    WebSocket,
    gRPC
}

public class RequestTab : INotifyPropertyChanged
{
    private string _name = "New Request";
    private RequestType _requestType = RequestType.HTTP;
    private bool _isSelected;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}