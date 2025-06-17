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
using Drasi.Reactions.McpQueryResultsServer;
using Drasi.Reactions.McpQueryResultsServer.Handlers;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reactions.McpQueryResultsServer.Services;
using Microsoft.Extensions.DependencyInjection;

var reaction = new ReactionBuilder<QueryConfig>()
    .UseChangeEventHandler<ChangeEventHandler>()
    .UseControlEventHandler<ControlEventHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices(services => 
    {
        // Core services
        services.AddSingleton<IQueryResultStore, QueryResultStore>();
        services.AddSingleton<IErrorStateHandler, ErrorStateHandler>();
        services.AddSingleton<IQueryConfigValidationService, QueryConfigValidationService>();
        
        // MCP session tracking and notifications
        services.AddSingleton<ISessionTracker, SessionTracker>();
        services.AddSingleton<IMcpNotificationService, McpNotificationService>();
        
        // MCP server as hosted service
        services.AddHostedService<McpServerHostedService>();
    })
    .Build();

try
{
    // Validate query configurations before starting
    var validationService = reaction.Services.GetRequiredService<IQueryConfigValidationService>();
    await validationService.ValidateQueryConfigsAsync(CancellationToken.None);
    
    // Start MCP server first (as background service)
    // The McpServerHostedService will start automatically
    
    // Start the Drasi reaction
    await reaction.StartAsync();
}
catch (Exception ex)
{
    var errorHandler = reaction.Services.GetRequiredService<IErrorStateHandler>();
    errorHandler.Terminate($"Fatal error starting reaction: {ex.Message}");
}