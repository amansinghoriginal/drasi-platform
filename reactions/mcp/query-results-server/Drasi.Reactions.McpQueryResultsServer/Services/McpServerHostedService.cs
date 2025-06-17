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
                .WithResources<DrasiResources>()
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