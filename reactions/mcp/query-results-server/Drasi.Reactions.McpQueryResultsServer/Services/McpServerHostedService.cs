// Copyright 2025 The Drasi Authors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol; // For McpException
using Drasi.Reactions.McpQueryResultsServer.Mcp;
using Drasi.Reaction.SDK.Services;
using Microsoft.Extensions.DependencyInjection; // Required for GetRequiredService

namespace Drasi.Reactions.McpQueryResultsServer.Services;

public class McpServerHostedService : IHostedService
{
    private WebApplication? _mcpApp;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpServerHostedService> _logger;
    private readonly IErrorStateHandler _errorStateHandler;
    private readonly ISessionTracker _sessionTracker;
    
    public McpServerHostedService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<McpServerHostedService> logger,
        IErrorStateHandler errorStateHandler,
        ISessionTracker sessionTracker)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _errorStateHandler = errorStateHandler;
        _sessionTracker = sessionTracker;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var builder = WebApplication.CreateBuilder();
            
            // Configure MCP server port
            var mcpPortSetting = _configuration["mcpServerPort"] ?? "8080";
            if (!int.TryParse(mcpPortSetting, out var mcpPort))
            {
                mcpPort = 8080;
                _logger.LogWarning("mcpServerPort configuration is invalid ('{McpPortSetting}'). Defaulting to {DefaultPort}.", mcpPortSetting, mcpPort);
            }
            
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.ListenAnyIP(mcpPort);
            });
            
            // Configure logging to match Drasi reaction
            builder.Logging.ClearProviders(); 
            builder.Logging.AddConsole(); 
            
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInfo = new Implementation
                    {
                        Name = "drasi-mcp-query-results", 
                        Version = "1.0.0" 
                    };
                })
                .WithHttpTransport(httpOptions =>
                {
                    httpOptions.RunSessionHandler = async (httpContext, mcpServer, cancellationToken) =>
                    {
                        var sessionId = httpContext.TraceIdentifier; // Unique identifier for this session
                        _sessionTracker.AddSession(sessionId, mcpServer);
                        
                        try
                        {
                            // Run the MCP server for this session
                            await mcpServer.RunAsync(cancellationToken);
                        }
                        finally
                        {
                            // Remove session when it ends
                            _sessionTracker.RemoveSession(sessionId);
                        }
                    };
                })
                // Not using .WithResources<DrasiResources>() to avoid automatic template discovery
                .WithListResourcesHandler(async (request, ct) =>
                {
                    var resources = new List<Resource>();
                    var queryConfigService = _serviceProvider.GetRequiredService<IQueryConfigService>();
                    
                    // Add static resources for each configured query
                    var queryNames = queryConfigService.GetQueryNames();
                    foreach (var queryId in queryNames)
                    {
                        var config = queryConfigService.GetQueryConfig<Models.QueryConfig>(queryId);
                        resources.Add(new Resource
                        {
                            Uri = $"drasi://queries/{queryId}",
                            Name = queryId,
                            Description = config?.Description ?? $"Live dataset: {queryId}",
                            MimeType = "application/json"
                        });
                    }
                    
                    await Task.CompletedTask; // Satisfy async requirement
                    return new ListResourcesResult { Resources = resources };
                })
                .WithListResourceTemplatesHandler(async (request, ct) =>
                {
                    // Only return entry templates, not query templates
                    // The SDK will automatically include templates from DrasiResources,
                    // so we need to filter out the query template
                    var templates = new List<ResourceTemplate>
                    {
                        new ResourceTemplate
                        {
                            UriTemplate = "drasi://entries/{queryId}/{entryKey}",
                            Name = "Dataset Entry",
                            Description = "Access a specific item from a live dataset",
                            MimeType = "application/json"
                        }
                    };
                    
                    await Task.CompletedTask; // Satisfy async requirement
                    return new ListResourceTemplatesResult { ResourceTemplates = templates };
                })
                .WithReadResourceHandler(async (request, ct) =>
                {
                    var uri = request.Params?.Uri;
                    if (string.IsNullOrEmpty(uri))
                    {
                        throw new McpException("Resource URI is required", McpErrorCode.InvalidParams);
                    }
                    
                    // Handle query resources (static)
                    if (uri.StartsWith("drasi://queries/"))
                    {
                        var queryId = uri.Substring("drasi://queries/".Length);
                        var drasiResources = new DrasiResources(
                            _serviceProvider.GetRequiredService<IQueryResultStore>(),
                            _serviceProvider.GetRequiredService<IQueryConfigService>(),
                            _serviceProvider.GetRequiredService<ILogger<DrasiResources>>()
                        );
                        var result = await drasiResources.GetQueryResource(queryId, ct);
                        return new ReadResourceResult { Contents = new List<ResourceContents> { result } };
                    }
                    
                    // Handle entry resources (templated)
                    if (uri.StartsWith("drasi://entries/"))
                    {
                        var parts = uri.Substring("drasi://entries/".Length).Split('/', 2);
                        if (parts.Length != 2)
                        {
                            throw new McpException("Invalid entry resource URI format", McpErrorCode.InvalidParams);
                        }
                        
                        var queryId = parts[0];
                        var entryKey = parts[1];
                        var drasiResources = new DrasiResources(
                            _serviceProvider.GetRequiredService<IQueryResultStore>(),
                            _serviceProvider.GetRequiredService<IQueryConfigService>(),
                            _serviceProvider.GetRequiredService<ILogger<DrasiResources>>()
                        );
                        var result = await drasiResources.GetEntry(queryId, entryKey, ct);
                        return new ReadResourceResult { Contents = new List<ResourceContents> { result } };
                    }
                    
                    throw new McpException($"Unknown resource URI: {uri}", McpErrorCode.InvalidParams);
                })
                .WithListToolsHandler(async (request, ct) =>
                {
                    var tools = new List<Tool>();
                    var queryConfigService = _serviceProvider.GetRequiredService<IQueryConfigService>();
                    
                    // Create a tool for each configured query
                    var queryNames = queryConfigService.GetQueryNames();
                    foreach (var queryId in queryNames)
                    {
                        var config = queryConfigService.GetQueryConfig<Models.QueryConfig>(queryId);
                        tools.Add(new Tool
                        {
                            Name = $"get_{queryId}_results",
                            Description = config?.Description ?? $"Fetch live {queryId} data",
                            InputSchema = System.Text.Json.JsonSerializer.SerializeToElement(new
                            {
                                type = "object",
                                properties = new
                                {
                                    limit = new
                                    {
                                        type = "integer",
                                        description = "Maximum number of results to return (optional, default: all)",
                                        minimum = 1
                                    },
                                    filter = new
                                    {
                                        type = "object",
                                        description = "Optional filter criteria as key-value pairs",
                                        additionalProperties = true
                                    }
                                },
                                additionalProperties = false
                            })
                        });
                    }
                    
                    await Task.CompletedTask;
                    return new ListToolsResult { Tools = tools };
                })
                .WithCallToolHandler(async (request, ct) =>
                {
                    var toolName = request.Params?.Name;
                    if (string.IsNullOrEmpty(toolName))
                    {
                        throw new McpException("Tool name is required", McpErrorCode.InvalidParams);
                    }
                    
                    // Extract query ID from tool name (format: get_{queryId}_results)
                    if (toolName.StartsWith("get_") && toolName.EndsWith("_results"))
                    {
                        var queryId = toolName.Substring(4, toolName.Length - 12); // Remove "get_" and "_results"
                        
                        var queryConfigService = _serviceProvider.GetRequiredService<IQueryConfigService>();
                        var queryResultStore = _serviceProvider.GetRequiredService<IQueryResultStore>();
                        
                        // Verify query exists
                        var config = queryConfigService.GetQueryConfig<Models.QueryConfig>(queryId);
                        if (config == null)
                        {
                            throw new McpException($"Query '{queryId}' not found", McpErrorCode.InvalidParams);
                        }
                        
                        // Parse tool arguments
                        var args = request.Params?.Arguments;
                        int? limit = null;
                        Dictionary<string, object>? filter = null;
                        
                        if (args != null)
                        {
                            var argsElement = System.Text.Json.JsonSerializer.SerializeToElement(args);
                            
                            if (argsElement.TryGetProperty("limit", out var limitElement))
                            {
                                limit = limitElement.GetInt32();
                            }
                            
                            if (argsElement.TryGetProperty("filter", out var filterElement))
                            {
                                filter = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(filterElement.GetRawText());
                            }
                        }
                        
                        // Get all entries for the query
                        var entryKeys = await queryResultStore.GetQueryEntriesAsync(queryId);
                        _logger.LogInformation("Tool {ToolName} called for query {QueryId}. Found {EntryCount} entries.", 
                            toolName, queryId, entryKeys.Count());
                        var results = new List<object>();
                        
                        foreach (var entryKey in entryKeys)
                        {
                            var entry = await queryResultStore.GetEntryAsync(queryId, entryKey);
                            if (entry.HasValue)
                            {
                                _logger.LogDebug("Processing entry {EntryKey} for query {QueryId}", entryKey, queryId);
                                // Apply filter if provided
                                if (filter != null && filter.Count > 0)
                                {
                                    bool matches = true;
                                    foreach (var filterKv in filter)
                                    {
                                        if (entry.Value.TryGetProperty(filterKv.Key, out var entryValue))
                                        {
                                            var entryValueStr = entryValue.ToString();
                                            var filterValueStr = filterKv.Value?.ToString();
                                            if (!string.Equals(entryValueStr, filterValueStr, StringComparison.OrdinalIgnoreCase))
                                            {
                                                matches = false;
                                                break;
                                            }
                                        }
                                        else
                                        {
                                            matches = false;
                                            break;
                                        }
                                    }
                                    
                                    if (!matches) continue;
                                }
                                
                                results.Add(System.Text.Json.JsonSerializer.Deserialize<object>(entry.Value.GetRawText())!);
                                
                                // Apply limit if specified
                                if (limit.HasValue && results.Count >= limit.Value)
                                {
                                    break;
                                }
                            }
                        }
                        
                        // Create response
                        var response = new
                        {
                            queryId = queryId,
                            description = config.Description,
                            resultCount = results.Count,
                            totalCount = entryKeys.Count(),
                            results = results
                        };
                        
                        return new CallToolResponse
                        {
                            Content = new List<Content>
                            {
                                new Content
                                {
                                    Type = "text",
                                    Text = System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions
                                    {
                                        WriteIndented = true
                                    })
                                }
                            }
                        };
                    }
                    
                    throw new McpException($"Unknown tool: {toolName}", McpErrorCode.MethodNotFound);
                })
                .WithListPromptsHandler(async (request, ct) =>
                {
                    // Return empty prompts list - we don't have any prompts
                    await Task.CompletedTask;
                    return new ListPromptsResult { Prompts = new List<Prompt>() };
                })
                .WithSubscribeToResourcesHandler(async (request, ct) =>
                {
                    _logger.LogDebug("Client subscribed to {Uri}", request.Params?.Uri);
                    await Task.CompletedTask; // Satisfy async requirement
                    return new EmptyResult();
                })
                .WithUnsubscribeFromResourcesHandler(async (request, ct) =>
                {
                    _logger.LogDebug("Client unsubscribed from {Uri}", request.Params?.Uri);
                    await Task.CompletedTask; // Satisfy async requirement
                    return new EmptyResult();
                });
            
            builder.Services.AddSingleton(_serviceProvider.GetRequiredService<IQueryResultStore>());
            builder.Services.AddSingleton(_serviceProvider.GetRequiredService<IQueryConfigService>());
            builder.Services.AddSingleton(_configuration); 
            
            _mcpApp = builder.Build();
            
            // Map MCP endpoints - this sets up both SSE and HTTP endpoints
            _mcpApp.MapMcp("/mcp");

            var endpointDataSource = _mcpApp as IEndpointRouteBuilder;
            if (endpointDataSource != null)
            {
                _logger.LogInformation("Registered MCP endpoints:");
                foreach (var endpoint in endpointDataSource.DataSources.SelectMany(s => s.Endpoints))
                {
                    if (endpoint is RouteEndpoint routeEndpoint)
                    {
                        _logger.LogInformation("  Route: {Pattern}, HTTP Methods: {Methods}", 
                            routeEndpoint.RoutePattern.RawText,
                            routeEndpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["ANY"]);
                    }
                }
            }
            
            _logger.LogInformation("Starting MCP server on port {Port}", mcpPort);
            await _mcpApp.StartAsync(cancellationToken);
            _logger.LogInformation("MCP server started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server");
            _errorStateHandler.Terminate($"Failed to start MCP server: {ex.Message}");
            throw; 
        }
    }
    
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_mcpApp != null)
        {
            _logger.LogInformation("Stopping MCP server");
            await _mcpApp.StopAsync(cancellationToken);
        }
    }
}