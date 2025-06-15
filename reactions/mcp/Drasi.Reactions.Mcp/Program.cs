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

using Drasi.Reaction.SDK;
using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.Mcp.Interfaces;
using Drasi.Reactions.Mcp.Models;
using Drasi.Reactions.Mcp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Build the Drasi reaction with MCP server as hosted service
var reaction = new ReactionBuilder<McpQueryConfig>()
    .UseChangeEventHandler<McpChangeHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices((services) =>
    {
        // Core services - required for startup validation and initialization
        services.AddSingleton<IQueryConfigValidationService, QueryConfigValidationService>();
        services.AddSingleton<IErrorStateHandler, ErrorStateHandler>();
        services.AddHttpClient();
        
        // Extended management client for query readiness checking
        services.AddSingleton<IExtendedManagementClient, ExtendedManagementClient>();
        
        // Sync point management for sequence tracking
        services.AddSingleton<IMcpSyncPointManager, McpSyncPointManager>();
        
        // Resource storage - shared between Drasi and MCP servers
        services.AddSingleton<IMcpResourceStore, InMemoryMcpResourceStore>();
        
        // Query initialization service
        services.AddSingleton<QueryInitializationService>();
        
        // MCP-specific services
        services.AddSingleton<McpResourceProvider>();
        services.AddSingleton<IMcpNotifier, McpNotifier>();
        // TODO: Add notification service when ready
        //services.AddSingleton<McpNotificationService>();
        //services.AddHostedService<McpNotificationService>(provider => provider.GetRequiredService<McpNotificationService>());
        
        // Register MCP server as hosted service
        services.AddHostedService<McpServerHostedService>();
    })
    .Build();

// Get services for validation and initialization
var logger = reaction.Services.GetRequiredService<ILogger<Program>>();
var validator = reaction.Services.GetRequiredService<IQueryConfigValidationService>();
var errorHandler = reaction.Services.GetRequiredService<IErrorStateHandler>();

try
{
    // Validate all query configurations
    logger.LogInformation("Validating query configurations");
    if (!await validator.ValidateAllAsync())
    {
        throw new InvalidOperationException("Query configuration validation failed");
    }
    
    // Initialize queries and sync initial data
    logger.LogInformation("Initializing queries and syncing initial data");
    var initializer = reaction.Services.GetRequiredService<QueryInitializationService>();
    await initializer.InitializeAllQueriesAsync();
    
    logger.LogInformation("Starting Drasi MCP Reaction");
    
    // Start the reaction (this runs both Dapr listener on port 80 and MCP server on port 8080)
    await reaction.StartAsync();
}
catch (Exception ex)
{
    await errorHandler.HandleFatalErrorAsync(ex, "Failed to start Drasi MCP Reaction");
}