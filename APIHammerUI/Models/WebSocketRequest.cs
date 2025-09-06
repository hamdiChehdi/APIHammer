using System.Collections.ObjectModel;
using APIHammerUI.Helpers;

namespace APIHammerUI.Models;

public class WebSocketRequest : ObservableObject
{
    private string _url = "";
    private string _message = "";
    private string _messages = "";
    private bool _isConnected;
    private bool _isConnecting;

    public WebSocketRequest()
    {
        Headers = new ObservableCollection<HttpHeaderItem>
        {
            new() { Key = "", Value = "", IsEnabled = true }
        };
    }

    public string Url
    {
        get => _url;
        set
        {
            _url = value;
            OnPropertyChanged(nameof(Url));
        }
    }

    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            OnPropertyChanged(nameof(Message));
        }
    }

    public string Messages
    {
        get => _messages;
        set
        {
            _messages = value;
            OnPropertyChanged(nameof(Messages));
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _isConnecting = value;
            OnPropertyChanged(nameof(IsConnecting));
            OnPropertyChanged(nameof(ConnectButtonText));
        }
    }

    public ObservableCollection<HttpHeaderItem> Headers { get; set; }

    public string ConnectButtonText => IsConnecting ? "Connecting..." : (IsConnected ? "Disconnect" : "Connect");
}