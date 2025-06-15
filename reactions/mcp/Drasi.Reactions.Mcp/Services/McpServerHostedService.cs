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

using Drasi.Reactions.Mcp.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Drasi.Reactions.Mcp.Services;

/// <summary>
/// Hosted service that runs the MCP server on a separate port
/// </summary>
public class McpServerHostedService : IHostedService
{
    private WebApplication? _mcpApp;
    private readonly IMcpResourceStore _resourceStore;
    private readonly McpResourceProvider _resourceProvider;
    private readonly ILogger<McpServerHostedService> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly IMcpNotifier _notifier;
    private readonly IServiceProvider _serviceProvider;

    public McpServerHostedService(
        IMcpResourceStore resourceStore,
        McpResourceProvider resourceProvider,
        ILogger<McpServerHostedService> logger,
        IHostApplicationLifetime applicationLifetime,
        IMcpNotifier notifier,
        IServiceProvider serviceProvider)
    {
        _resourceStore = resourceStore;
        _resourceProvider = resourceProvider;
        _logger = logger;
        _applicationLifetime = applicationLifetime;
        _notifier = notifier;
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var mcpPort = Environment.GetEnvironmentVariable("MCP_PORT") ?? "8080";
        _logger.LogInformation("Starting MCP server on port {Port}", mcpPort);

        try
        {
            // Create a separate web application for MCP
            var builder = WebApplication.CreateBuilder();
            
            // Configure to listen on MCP port
            builder.WebHost.UseUrls($"http://0.0.0.0:{mcpPort}");
            
            // Configure logging to match main application
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            
            // Register services needed by MCP
            builder.Services.AddSingleton(_resourceStore);
            builder.Services.AddSingleton(_resourceProvider);
            builder.Services.AddSingleton(_notifier);
            
            // TODO: Get the notification service from the parent service provider
            //var notificationService = _serviceProvider.GetService<McpNotificationService>();
            //if (notificationService != null)
            //{
            //    builder.Services.AddSingleton(notificationService);
            //}
            
            // Initialize the static MCP type services
            var serviceProvider = builder.Services.BuildServiceProvider();
            var toolTypeLogger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<McpToolTypeService>();
            McpToolTypeService.Initialize(_resourceStore, toolTypeLogger);
            
            // Configure MCP SDK
            var reactionName = Environment.GetEnvironmentVariable("DRASI_REACTION_NAME") ?? "mcp-reaction";
            
            // TODO: Use official MCP SDK when available
            // builder.Services.AddMcpServer(options =>
            // {
            //     options.ServerInfo = new()
            //     {
            //         Name = "Drasi MCP Reaction",
            //         Version = "1.0.0"
            //     };
            // })
            // .WithHttpTransport()
            // .WithTools<McpToolTypeService>()
            // ;
            
            _mcpApp = builder.Build();
            
            // TODO: Set up notification service when ready
            
            // Configure routes
            _mcpApp.UseRouting();
            
            // Add a health check endpoint (outside of MCP)
            _mcpApp.MapGet("/health", () => new { status = "healthy", service = "mcp-server" });
            
            // Map MCP endpoints
            _mcpApp.MapMcp();
            
            // Start the MCP server
            await _mcpApp.StartAsync(cancellationToken);
            
            _logger.LogInformation("MCP server started successfully on port {Port}", mcpPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start MCP server");
            // Signal the main application to shut down if MCP server fails
            _applicationLifetime.StopApplication();
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping MCP server");
        
        if (_mcpApp != null)
        {
            try
            {
                await _mcpApp.StopAsync(cancellationToken);
                _logger.LogInformation("MCP server stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping MCP server");
            }
            finally
            {
                if (_mcpApp is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}