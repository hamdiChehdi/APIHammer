using System.ComponentModel;

namespace APIHammerUI.Models;

public class GrpcRequest : INotifyPropertyChanged
{
    private string _server = "";
    private string _service = "";
    private string _method = "";
    private string _request = "";
    private string _response = "";
    private bool _isLoading;

    public string Server
    {
        get => _server;
        set
        {
            _server = value;
            OnPropertyChanged(nameof(Server));
        }
    }

    public string Service
    {
        get => _service;
        set
        {
            _service = value;
            OnPropertyChanged(nameof(Service));
        }
    }

    public string Method
    {
        get => _method;
        set
        {
            _method = value;
            OnPropertyChanged(nameof(Method));
        }
    }

    public string Request
    {
        get => _request;
        set
        {
            _request = value;
            OnPropertyChanged(nameof(Request));
        }
    }

    public string Response
    {
        get => _response;
        set
        {
            _response = value;
            OnPropertyChanged(nameof(Response));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}