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

using Drasi.Reactions.McpServer.Resources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Drasi.Reactions.McpServer.Services;

/// <summary>
/// Hosted service that runs the MCP server on port 8080.
/// </summary>
public class McpServerHostedService : IHostedService
{
    private readonly ILogger<McpServerHostedService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private IHost? _mcpHost;

    public McpServerHostedService(
        ILogger<McpServerHostedService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting MCP Server on port 8080");

        // Create a separate web application for MCP
        var builder = WebApplication.CreateBuilder();
        
        // Configure to listen only on port 8080
        builder.WebHost.UseUrls("http://+:8080");
        
        // Configure logging to match parent application
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Information;
        });

        // Register shared services from parent DI container
        // The resource store is shared between both services
        builder.Services.AddSingleton(_serviceProvider.GetRequiredService<IResourceStoreService>());
        
        // Configure MCP server
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithResources<DrasiResourceType>();

        var app = builder.Build();

        // Configure MCP endpoints
        app.UseRouting();
        
        // Map MCP endpoints
        app.MapMcp("/mcp");
        
        // MCP Server info endpoint
        app.MapGet("/", () => new
        {
            name = "Drasi MCP Server",
            version = "1.0.0",
            description = "Model Context Protocol server for Drasi query results",
            endpoints = new
            {
                sse = "/mcp/sse",
                http = "/mcp"
            }
        });

        // Health check for MCP server
        app.MapGet("/health", () => "OK");

        // Start the MCP host
        _mcpHost = app;
        await _mcpHost.StartAsync(cancellationToken);
        
        _logger.LogInformation("MCP Server started successfully on port 8080");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MCP Server");
        
        if (_mcpHost != null)
        {
            await _mcpHost.StopAsync(cancellationToken);
            _mcpHost.Dispose();
        }
        
        _logger.LogInformation("MCP Server stopped");
    }
}