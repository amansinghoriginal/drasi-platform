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
using Drasi.Reactions.McpServer.Models;
using Drasi.Reactions.McpServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Build the Drasi reaction using the SDK's ReactionBuilder
var reaction = new ReactionBuilder<QueryConfig>()
    .UseChangeEventHandler<ChangeEventHandler>()
    .UseControlEventHandler<ControlSignalHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices(services => 
    {
        // Add shared services
        services.AddSingleton<IResourceStoreService, ResourceStoreService>();
        services.AddSingleton<IQueryInitializationService, QueryInitializationService>();
        services.AddSingleton<IConfigValidationService, ConfigValidationService>();
        
        // Add the MCP server as a hosted service (will run on port 8080)
        services.AddHostedService<McpServerHostedService>();
    })
    .Build();

try
{
    Console.WriteLine("Starting Drasi MCP Server Reaction");
    Console.WriteLine("- Port 80: Drasi event listener (Dapr)");
    Console.WriteLine("- Port 8080: MCP server (AI clients)");
    
    // Step 1: Validate query configurations
    Console.WriteLine("Validating query configurations");
    var configValidator = reaction.Services.GetRequiredService<IConfigValidationService>();
    if (!await configValidator.ValidateAll())
    {
        throw new InvalidOperationException("Configuration validation failed");
    }

    // Step 2: Initialize query data
    Console.WriteLine("Initializing query data");
    var queryInitializer = reaction.Services.GetRequiredService<IQueryInitializationService>();
    await queryInitializer.InitializeAllQueries();

    // Step 3: Start the reaction (listens on port 80 by default)
    // This will handle all Dapr integration automatically
    Console.WriteLine("Starting Drasi reaction service");
    await reaction.StartAsync();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error starting MCP Server reaction: {ex.Message}");
    Environment.Exit(1);
}