using APIHammerUI.Helpers;
using System.Collections.ObjectModel;

namespace APIHammerUI.Models;

public class GrpcRequest : ObservableObject
{
    private string _server = "";
    private string _protoFilePath = "";
    private string _service = "";
    private string _method = "";
    private string _request = "";
    private string _response = "";
    private bool _isLoading;

    public ObservableCollection<string> Services { get; } = new();
    public ObservableCollection<string> Methods { get; } = new();

    public string Server
    {
        get => _server;
        set { _server = value; OnPropertyChanged(nameof(Server)); }
    }

    public string ProtoFilePath
    {
        get => _protoFilePath;
        set { _protoFilePath = value; OnPropertyChanged(nameof(ProtoFilePath)); }
    }

    public string Service
    {
        get => _service;
        set { _service = value; OnPropertyChanged(nameof(Service)); }
    }

    public string Method
    {
        get => _method;
        set { _method = value; OnPropertyChanged(nameof(Method)); }
    }

    public string Request
    {
        get => _request;
        set { _request = value; OnPropertyChanged(nameof(Request)); }
    }

    public string Response
    {
        get => _response;
        set { _response = value; OnPropertyChanged(nameof(Response)); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
    }

    public void SetServices(IEnumerable<string> services)
    {
        Services.Clear();
        foreach (var s in services) Services.Add(s);
    }

    public void SetMethods(IEnumerable<string> methods)
    {
        Methods.Clear();
        foreach (var m in methods) Methods.Add(m);
    }
}