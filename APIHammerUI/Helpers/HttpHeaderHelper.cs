using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace APIHammerUI.Helpers;

public static class HttpHeaderHelper
{
    private static Dictionary<string, List<string>>? _headerValueSuggestions;

    public static Dictionary<string, List<string>> HeaderValueSuggestions
    {
        get
        {
            if (_headerValueSuggestions == null)
            {
                var assembly = Assembly.GetExecutingAssembly();
                if (assembly is null)
                {
                    throw new Exception("Can't load ExecutingAssembly ");
                }
                using (Stream? stream = assembly.GetManifestResourceStream("APIHammerUI.Resources.HttpHeaderSuggestions.json"))
                {
                    if (stream == null)
                    {
                        throw new Exception("Resource file 'HttpHeaderSuggestions.json' not found in embedded resources.");
                    }

                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string jsonContent = reader.ReadToEnd();
                        _headerValueSuggestions = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(jsonContent);
                    }
                }
            }
            return _headerValueSuggestions!;
        }
    }

    public static string GetDefaultValueForHeader(string headerName)
        => HeaderValueSuggestions.TryGetValue(headerName, out var suggestions) && suggestions.Any() ? suggestions.First() : headerName switch
        {
            "Content-Type" => "application/json",
            "Accept" => "application/json",
            "Accept-Encoding" => "gzip, deflate",
            "Cache-Control" => "no-cache",
            "Connection" => "keep-alive",
            "User-Agent" => "API Hammer/1.0",
            _ => ""
        };
}