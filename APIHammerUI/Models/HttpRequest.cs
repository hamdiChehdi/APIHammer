using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace APIHammerUI.Models;

public enum AuthenticationType
{
    None,
    BasicAuth,
    BearerToken,
    ApiKey
}

public class AuthenticationSettings : INotifyPropertyChanged
{
    private AuthenticationType _type = AuthenticationType.None;
    private string _username = "";
    private string _password = "";
    private string _token = "";
    private string _apiKeyHeader = "X-API-Key";
    private string _apiKeyValue = "";

    public AuthenticationType Type
    {
        get => _type;
        set
        {
            _type = value;
            OnPropertyChanged(nameof(Type));
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            _username = value;
            OnPropertyChanged(nameof(Username));
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            _password = value;
            OnPropertyChanged(nameof(Password));
        }
    }

    public string Token
    {
        get => _token;
        set
        {
            _token = value;
            OnPropertyChanged(nameof(Token));
        }
    }

    public string ApiKeyHeader
    {
        get => _apiKeyHeader;
        set
        {
            _apiKeyHeader = value;
            OnPropertyChanged(nameof(ApiKeyHeader));
        }
    }

    public string ApiKeyValue
    {
        get => _apiKeyValue;
        set
        {
            _apiKeyValue = value;
            OnPropertyChanged(nameof(ApiKeyValue));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class HttpHeaderItem : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";
    private bool _isEnabled = true;

    public string Key
    {
        get => _key;
        set
        {
            _key = value;
            OnPropertyChanged(nameof(Key));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class HttpQueryParameter : INotifyPropertyChanged
{
    private string _key = "";
    private string _value = "";
    private bool _isEnabled = true;

    public string Key
    {
        get => _key;
        set
        {
            _key = value;
            OnPropertyChanged(nameof(Key));
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class HttpRequest : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    // Streaming chunks (incremental, used to display while downloading)
    private ObservableCollection<string> _responseChunks = new();
    public ObservableCollection<string> ResponseChunks
    {
        get => _responseChunks;
        set
        {
            _responseChunks = value;
            OnPropertyChanged(nameof(ResponseChunks));
        }
    }
    private string _method = "GET";
    private string _url = "";
    private string _body = "";
    private string _response = "";
    private string _trucatedResponse = "truncated response";
    private bool _isLoading;
    private ObservableCollection<HttpHeaderItem> _headers;
    private ObservableCollection<HttpQueryParameter> _queryParameters;
    private AuthenticationSettings _authentication;
    
    // Response metadata properties
    private TimeSpan? _responseTime;
    private long? _responseSize;
    private DateTime? _requestDateTime;

    public HttpRequest()
    {
        _headers = new ObservableCollection<HttpHeaderItem>();
        _queryParameters = new ObservableCollection<HttpQueryParameter>();
        _authentication = new AuthenticationSettings();
        
        // Add empty header row only
        _headers.Add(new HttpHeaderItem { Key = "", Value = "" }); // Empty row for new entries
        
        // Add empty query parameter row
        var emptyParam = new HttpQueryParameter { Key = "", Value = "" };
        emptyParam.PropertyChanged += QueryParameter_PropertyChanged;
        _queryParameters.Add(emptyParam);

        // Subscribe to collection changes to wire up property change events
        _queryParameters.CollectionChanged += (sender, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (HttpQueryParameter item in e.NewItems)
                {
                    item.PropertyChanged += QueryParameter_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (HttpQueryParameter item in e.OldItems)
                {
                    item.PropertyChanged -= QueryParameter_PropertyChanged;
                }
            }
            OnPropertyChanged(nameof(FullUrl));
        };
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

    public string Url
    {
        get => _url;
        set
        {
            _url = value;
            OnPropertyChanged(nameof(Url));
            OnPropertyChanged(nameof(FullUrl)); // Update full URL when base URL changes
        }
    }

    // Computed property that combines base URL with query parameters
    public string FullUrl
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_url))
                return _url;

            var enabledParams = _queryParameters
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.Key))
                .ToList();

            if (!enabledParams.Any())
                return _url;

            var baseUrl = _url.Contains('?') ? _url : _url;
            var queryString = string.Join("&", enabledParams.Select(p => 
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value ?? "")}"));

            var separator = _url.Contains('?') ? "&" : "?";
            return $"{baseUrl}{separator}{queryString}";
        }
    }

    public ObservableCollection<HttpHeaderItem> Headers
    {
        get => _headers;
        set
        {
            _headers = value;
            OnPropertyChanged(nameof(Headers));
        }
    }

    public ObservableCollection<HttpQueryParameter> QueryParameters
    {
        get => _queryParameters;
        set
        {
            if (_queryParameters != null)
            {
                foreach (var param in _queryParameters)
                {
                    param.PropertyChanged -= QueryParameter_PropertyChanged;
                }
            }

            _queryParameters = value;
            
            if (_queryParameters != null)
            {
                foreach (var param in _queryParameters)
                {
                    param.PropertyChanged += QueryParameter_PropertyChanged;
                }
            }

            OnPropertyChanged(nameof(QueryParameters));
            OnPropertyChanged(nameof(FullUrl));
        }
    }

    public AuthenticationSettings Authentication
    {
        get => _authentication;
        set
        {
            _authentication = value;
            OnPropertyChanged(nameof(Authentication));
        }
    }

    private void QueryParameter_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update FullUrl when any query parameter changes
        OnPropertyChanged(nameof(FullUrl));
    }

    public string Body
    {
        get => _body;
        set
        {
            _body = value;
            OnPropertyChanged(nameof(Body));
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

    public string TruncatedResponse
    {
        get => _trucatedResponse;
        set
        {
            _trucatedResponse = value;
            OnPropertyChanged(nameof(TruncatedResponse));
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

    // Response metadata properties
    public TimeSpan? ResponseTime
    {
        get => _responseTime;
        set
        {
            _responseTime = value;
            OnPropertyChanged(nameof(ResponseTime));
            OnPropertyChanged(nameof(ResponseTimeFormatted));
        }
    }

    public long? ResponseSize
    {
        get => _responseSize;
        set
        {
            _responseSize = value;
            OnPropertyChanged(nameof(ResponseSize));
            OnPropertyChanged(nameof(ResponseSizeFormatted));
        }
    }

    public DateTime? RequestDateTime
    {
        get => _requestDateTime;
        set
        {
            _requestDateTime = value;
            OnPropertyChanged(nameof(RequestDateTime));
            OnPropertyChanged(nameof(RequestDateTimeFormatted));
        }
    }

    // Formatted properties for display
    public string ResponseTimeFormatted => ResponseTime?.TotalMilliseconds.ToString("F0") + " ms" ?? "--";
    
    public string ResponseSizeFormatted
    {
        get
        {
            if (!ResponseSize.HasValue) return "--";
            
            var size = ResponseSize.Value;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / (1024.0 * 1024.0):F1} MB";
        }
    }
    
    public string RequestDateTimeFormatted => RequestDateTime?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "--";

    public List<string> Methods { get; } = new List<string> { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

    public List<AuthenticationType> AuthenticationTypes { get; } = new List<AuthenticationType> 
    { 
        AuthenticationType.None, 
        AuthenticationType.BasicAuth, 
        AuthenticationType.BearerToken, 
        AuthenticationType.ApiKey 
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}