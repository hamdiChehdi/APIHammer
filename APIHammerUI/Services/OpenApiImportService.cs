using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using APIHammerUI.Models;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Newtonsoft.Json;

namespace APIHammerUI.Services;

public class OpenApiImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<HttpRequest> ImportedRequests { get; set; } = new();
    public string CollectionName { get; set; } = "Imported Collection";
    public OpenApiInfo? ApiInfo { get; set; }
}

public class OpenApiImportService
{
    private static readonly HttpClient httpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(1)
    };

    /// <summary>
    /// Import requests from OpenAPI JSON file
    /// </summary>
    public async Task<OpenApiImportResult> ImportFromFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new OpenApiImportResult
                {
                    Success = false,
                    ErrorMessage = "File does not exist."
                };
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            return ParseOpenApiContent(jsonContent, Path.GetFileNameWithoutExtension(filePath));
        }
        catch (Exception ex)
        {
            return new OpenApiImportResult
            {
                Success = false,
                ErrorMessage = $"Error reading file: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Import requests from OpenAPI JSON URL
    /// </summary>
    public async Task<OpenApiImportResult> ImportFromUrlAsync(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new OpenApiImportResult
                {
                    Success = false,
                    ErrorMessage = "Invalid URL format."
                };
            }

            var response = await httpClient.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            
            var jsonContent = await response.Content.ReadAsStringAsync();
            var collectionName = uri.Host.Replace("www.", "");
            
            return ParseOpenApiContent(jsonContent, collectionName);
        }
        catch (HttpRequestException ex)
        {
            return new OpenApiImportResult
            {
                Success = false,
                ErrorMessage = $"HTTP error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new OpenApiImportResult
            {
                Success = false,
                ErrorMessage = $"Error downloading from URL: {ex.Message}"
            };
        }
    }

    private OpenApiImportResult ParseOpenApiContent(string jsonContent, string defaultCollectionName)
    {
        try
        {
            // Parse OpenAPI document
            var reader = new OpenApiStringReader();
            var openApiDocument = reader.Read(jsonContent, out var diagnostic);
            
            if (diagnostic.Errors.Any())
            {
                var errorMessages = string.Join(", ", diagnostic.Errors.Select(e => e.Message));
                return new OpenApiImportResult
                {
                    Success = false,
                    ErrorMessage = $"OpenAPI parsing errors: {errorMessages}"
                };
            }

            var result = new OpenApiImportResult
            {
                Success = true,
                ApiInfo = openApiDocument.Info,
                CollectionName = openApiDocument.Info?.Title ?? defaultCollectionName
            };

            // Get base URL(s) from servers
            var baseUrls = GetBaseUrls(openApiDocument);
            var defaultBaseUrl = baseUrls.FirstOrDefault() ?? "";

            // Convert paths to HTTP requests
            foreach (var path in openApiDocument.Paths)
            {
                foreach (var operation in path.Value.Operations)
                {
                    var httpRequest = CreateHttpRequestFromOperation(
                        path.Key, 
                        operation.Key, 
                        operation.Value, 
                        defaultBaseUrl,
                        openApiDocument.Components?.SecuritySchemes
                    );
                    
                    result.ImportedRequests.Add(httpRequest);
                }
            }

            return result;
        }
        catch (JsonException ex)
        {
            return new OpenApiImportResult
            {
                Success = false,
                ErrorMessage = $"Invalid JSON format: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new OpenApiImportResult
            {
                Success = false,
                ErrorMessage = $"Error parsing OpenAPI document: {ex.Message}"
            };
        }
    }

    private List<string> GetBaseUrls(OpenApiDocument document)
    {
        var baseUrls = new List<string>();
        
        if (document.Servers?.Any() == true)
        {
            baseUrls.AddRange(document.Servers.Select(s => s.Url));
        }
        
        return baseUrls;
    }

    private HttpRequest CreateHttpRequestFromOperation(
        string path, 
        OperationType operationType, 
        OpenApiOperation operation,
        string baseUrl,
        IDictionary<string, OpenApiSecurityScheme>? securitySchemes)
    {
        var httpRequest = new HttpRequest
        {
            Method = operationType.ToString().ToUpper(),
            Url = CombineUrlAndPath(baseUrl, path)
        };

        // Set operation name/summary as request name
        var operationName = operation.Summary ?? operation.OperationId ?? $"{operationType} {path}";
        // Note: We can't set the name directly on HttpRequest, this will be set on the RequestTab

        // Add headers from parameters
        var headerParams = operation.Parameters?
            .Where(p => p.In == ParameterLocation.Header)
            .ToList() ?? new List<OpenApiParameter>();

        foreach (var param in headerParams)
        {
            var headerItem = new HttpHeaderItem
            {
                Key = param.Name,
                Value = GetDefaultValueForParameter(param),
                IsEnabled = param.Required
            };
            httpRequest.Headers.Add(headerItem);
        }

        // Add query parameters
        var queryParams = operation.Parameters?
            .Where(p => p.In == ParameterLocation.Query)
            .ToList() ?? new List<OpenApiParameter>();

        foreach (var param in queryParams)
        {
            var queryParam = new HttpQueryParameter
            {
                Key = param.Name,
                Value = GetDefaultValueForParameter(param),
                IsEnabled = param.Required
            };
            httpRequest.QueryParameters.Add(queryParam);
        }

        // Set request body if present
        if (operation.RequestBody?.Content?.Any() == true)
        {
            var firstContent = operation.RequestBody.Content.First();
            var contentType = firstContent.Key;
            
            // Add Content-Type header
            var contentTypeHeader = httpRequest.Headers.FirstOrDefault(h => 
                h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            
            if (contentTypeHeader == null)
            {
                httpRequest.Headers.Add(new HttpHeaderItem
                {
                    Key = "Content-Type",
                    Value = contentType,
                    IsEnabled = true
                });
            }

            // Generate sample body based on schema
            if (firstContent.Value.Schema != null)
            {
                httpRequest.Body = GenerateSampleBody(firstContent.Value.Schema, contentType);
            }
        }

        // Setup authentication if security requirements are present
        SetupAuthentication(httpRequest, operation.Security, securitySchemes);

        return httpRequest;
    }

    private string CombineUrlAndPath(string baseUrl, string path)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return path;

        baseUrl = baseUrl.TrimEnd('/');
        path = path.TrimStart('/');
        
        return $"{baseUrl}/{path}";
    }

    private string GetDefaultValueForParameter(OpenApiParameter parameter)
    {
        // Return example value if available
        if (parameter.Example != null)
        {
            return parameter.Example.ToString() ?? "";
        }

        // Return default value if available
        if (parameter.Schema?.Default != null)
        {
            return parameter.Schema.Default.ToString() ?? "";
        }

        // Return enum first value if available
        if (parameter.Schema?.Enum?.Any() == true)
        {
            return parameter.Schema.Enum.First().ToString() ?? "";
        }

        // Return type-based default
        return parameter.Schema?.Type switch
        {
            "string" => "string",
            "integer" => "0",
            "number" => "0.0",
            "boolean" => "true",
            _ => ""
        };
    }

    private string GenerateSampleBody(OpenApiSchema schema, string contentType)
    {
        try
        {
            var sampleObject = GenerateSampleFromSchema(schema);
            
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return JsonConvert.SerializeObject(sampleObject, Formatting.Indented);
            }
            else if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
            {
                return "<root><!-- Sample XML body --></root>";
            }
            else if (contentType.Contains("form", StringComparison.OrdinalIgnoreCase))
            {
                // Generate form data
                if (sampleObject is Dictionary<string, object> dict)
                {
                    return string.Join("&", dict.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                }
            }
            
            return JsonConvert.SerializeObject(sampleObject, Formatting.Indented);
        }
        catch
        {
            return "{}";
        }
    }

    private object GenerateSampleFromSchema(OpenApiSchema schema)
    {
        if (schema.Example != null)
        {
            return schema.Example;
        }

        return schema.Type switch
        {
            "object" => GenerateObjectSample(schema),
            "array" => GenerateArraySample(schema),
            "string" => schema.Enum?.FirstOrDefault()?.ToString() ?? "string",
            "integer" => 0,
            "number" => 0.0,
            "boolean" => true,
            _ => schema.Default?.ToString() ?? "value"
        };
    }

    private Dictionary<string, object> GenerateObjectSample(OpenApiSchema schema)
    {
        var obj = new Dictionary<string, object>();
        
        if (schema.Properties?.Any() == true)
        {
            foreach (var property in schema.Properties)
            {
                obj[property.Key] = GenerateSampleFromSchema(property.Value);
            }
        }
        
        return obj;
    }

    private List<object> GenerateArraySample(OpenApiSchema schema)
    {
        var list = new List<object>();
        
        if (schema.Items != null)
        {
            list.Add(GenerateSampleFromSchema(schema.Items));
        }
        
        return list;
    }

    private void SetupAuthentication(
        HttpRequest httpRequest, 
        IList<OpenApiSecurityRequirement>? securityRequirements,
        IDictionary<string, OpenApiSecurityScheme>? securitySchemes)
    {
        if (securityRequirements?.Any() != true || securitySchemes?.Any() != true)
            return;

        var firstRequirement = securityRequirements.First();
        var firstScheme = firstRequirement.Keys.FirstOrDefault();
        
        if (firstScheme == null || !securitySchemes.TryGetValue(firstScheme.Reference?.Id ?? "", out var scheme))
            return;

        switch (scheme.Type)
        {
            case SecuritySchemeType.Http when scheme.Scheme?.Equals("bearer", StringComparison.OrdinalIgnoreCase) == true:
                httpRequest.Authentication.Type = AuthenticationType.BearerToken;
                httpRequest.Authentication.Token = "your-bearer-token-here";
                break;
                
            case SecuritySchemeType.Http when scheme.Scheme?.Equals("basic", StringComparison.OrdinalIgnoreCase) == true:
                httpRequest.Authentication.Type = AuthenticationType.BasicAuth;
                httpRequest.Authentication.Username = "username";
                httpRequest.Authentication.Password = "password";
                break;
                
            case SecuritySchemeType.ApiKey:
                httpRequest.Authentication.Type = AuthenticationType.ApiKey;
                httpRequest.Authentication.ApiKeyHeader = scheme.Name ?? "X-API-Key";
                httpRequest.Authentication.ApiKeyValue = "your-api-key-here";
                break;
        }
    }
}