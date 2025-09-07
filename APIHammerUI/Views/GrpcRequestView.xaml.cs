using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using APIHammerUI.Models;
using Microsoft.Win32;
using Grpc.Net.Client;
using System.Text.Json;
using System.Collections.Generic;
using Grpc.Core;

namespace APIHammerUI.Views;

public partial class GrpcRequestView : UserControl
{
    public GrpcRequestView()
    {
        InitializeComponent();
    }

    private void BrowseProto_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GrpcRequest grpcRequest)
            return;

        var dlg = new OpenFileDialog
        {
            Filter = "Proto files (*.proto)|*.proto|All files (*.*)|*.*",
            Multiselect = false
        };
        if (dlg.ShowDialog() == true)
        {
            grpcRequest.ProtoFilePath = dlg.FileName;
            try
            {
                LoadProto(grpcRequest, File.ReadAllText(dlg.FileName));
            }
            catch (Exception ex)
            {
                grpcRequest.Response = $"Failed to parse proto: {ex.Message}";
            }
        }
    }

    private void LoadProto(GrpcRequest grpcRequest, string protoContent)
    {
        var services = ProtoSimpleParser.ExtractServices(protoContent);
        grpcRequest.SetServices(services.Select(s => s.Name));
        grpcRequest.SetMethods(Array.Empty<string>());
        grpcRequest.Service = services.FirstOrDefault()?.Name ?? string.Empty;
        if (!string.IsNullOrEmpty(grpcRequest.Service))
        {
            var first = services.First(s => s.Name == grpcRequest.Service);
            grpcRequest.SetMethods(first.Methods.Select(m => m.Name));
            grpcRequest.Method = first.Methods.FirstOrDefault()?.Name ?? string.Empty;
        }
    }

    private async void CallButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GrpcRequest grpcRequest)
            return;

        try
        {
            grpcRequest.IsLoading = true;
            grpcRequest.Response = "Calling...";

            if (string.IsNullOrWhiteSpace(grpcRequest.Server))
                throw new InvalidOperationException("Server is required");
            if (string.IsNullOrWhiteSpace(grpcRequest.Service) || string.IsNullOrWhiteSpace(grpcRequest.Method))
                throw new InvalidOperationException("Service and Method are required");

            using var channel = GrpcChannel.ForAddress(grpcRequest.Server);
            await Task.Delay(300); // placeholder latency

            grpcRequest.Response = JsonSerializer.Serialize(new
            {
                info = "Placeholder - real dynamic invocation not yet implemented",
                server = grpcRequest.Server,
                service = grpcRequest.Service,
                method = grpcRequest.Method,
                requestJson = TryParseJson(grpcRequest.Request),
                guidance = new[]
                {
                    "To fully implement dynamic calls you need to compile the proto into descriptors",
                    "Then build Method<TReq,TRes> with real protobuf serializers",
                    "Serialize request using descriptors, invoke via CallInvoker.AsyncUnaryCall"
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            grpcRequest.Response = $"Error: {ex.Message}";
        }
        finally
        {
            grpcRequest.IsLoading = false;
        }
    }

    private static object? TryParseJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<JsonElement>(json); } catch { return json; }
    }
}

#region Proto parsing helpers
internal record ProtoService(string Name, List<ProtoMethod> Methods);
internal record ProtoMethod(string Name, string InputType, string OutputType, bool ClientStreaming, bool ServerStreaming);

internal static class ProtoSimpleParser
{
    public static List<ProtoService> ExtractServices(string protoContent)
    {
        var services = new List<ProtoService>();
        using var reader = new StringReader(protoContent);
        string? line;
        ProtoService? current = null;
        var methodList = new List<ProtoMethod>();
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("service "))
            {
                if (current != null)
                {
                    services.Add(current with { Methods = methodList });
                    methodList = new();
                }
                var name = line.Substring("service ".Length).Split('{', ' ').First().Trim();
                current = new ProtoService(name, new List<ProtoMethod>());
            }
            else if (current != null && line.StartsWith("rpc "))
            {
                try
                {
                    var afterRpc = line.Substring(4).Trim();
                    var name = afterRpc.Split('(')[0].Trim();
                    var input = afterRpc.Split('(')[1].Split(')')[0].Replace("stream", string.Empty).Trim();
                    var returnsPart = afterRpc.Split("returns")[1];
                    var output = returnsPart.Split('(')[1].Split(')')[0].Replace("stream", string.Empty).Trim();
                    bool clientStream = afterRpc.Contains("stream (");
                    bool serverStream = returnsPart.Contains("stream (");
                    methodList.Add(new ProtoMethod(name, input, output, clientStream, serverStream));
                }
                catch { }
            }
            else if (line.StartsWith("}"))
            {
                if (current != null)
                {
                    services.Add(current with { Methods = methodList });
                    current = null;
                    methodList = new();
                }
            }
        }
        if (current != null)
        {
            services.Add(current with { Methods = methodList });
        }
        return services;
    }
}
#endregion