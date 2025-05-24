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
using Drasi.Reactions.SyncQdrant;
using Microsoft.Extensions.DependencyInjection;
using Drasi.Reaction.SDK.Services;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Azure.AI.OpenAI;


var reaction = new ReactionBuilder<QueryConfig>()
    .UseChangeEventHandler<QdrantChangeEventHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices(services => 
    {
        services.AddHttpClient();
        services.AddSingleton<IExtendedManagementClient, ExtendedManagementClient>();
        services.AddSingleton<IErrorStateHandler, ErrorStateHandler>();
        services.AddSingleton<IQuerySyncPointManager, QuerySyncPointManager>();
        services.AddSingleton<IQueryInitializationService, QueryInitializationService>();

        // Validate and register ReactionConfig
        services.AddSingleton<IReactionConfigProvider, ReactionConfigProvider>();
        services.AddSingleton(sp =>
        {
            var provider = sp.GetRequiredService<IReactionConfigProvider>();
            return provider.GetValidatedReactionConfig();
        });

        // Register QdrantClient
        services.AddSingleton(sp => {
            var rc = sp.GetRequiredService<ReactionConfig>();
            var logger = sp.GetRequiredService<ILogger<QdrantClient>>();
            logger.LogInformation("Creating QdrantClient with Host: {Host}, Port: {Port}, Https: {Https}",
                                  rc.QdrantHost, rc.QdrantPort, rc.QdrantHttps);
            return new QdrantClient(
                host: rc.QdrantHost,
                port: rc.QdrantPort,
                https: rc.QdrantHttps
            );
        });

        // Register EmbeddingClient
        services.AddSingleton(sp =>
        {
            var rc = sp.GetRequiredService<ReactionConfig>();
            var logger = sp.GetRequiredService<ILogger<Program>>();

            logger.LogDebug("Creating AzureOpenAIClient with Endpoint: {Endpoint} ...", rc.AzureOpenAIEndpoint);
            var azureOpenAIClient = new AzureOpenAIClient(rc.AzureOpenAIEndpoint, rc.AzureOpenAIKey);
            logger.LogInformation("AzureOpenAIClient created successfully with Endpoint: {Endpoint}", rc.AzureOpenAIEndpoint);

            logger.LogDebug("Creating EmbeddingClient with ModelName: {ModelName} ...", rc.EmbeddingModelName);
            var embeddingClient = azureOpenAIClient.GetEmbeddingClient(rc.EmbeddingModelName);
            logger.LogInformation("EmbeddingClient created successfully with ModelName: {ModelName}", rc.EmbeddingModelName);

            return embeddingClient;
        });
    })
    .Build();

try
{
    var initializationService = reaction.Services.GetRequiredService<IQueryInitializationService>();
    await initializationService.InitializeQueriesAsync(CancellationToken.None);

    await reaction.StartAsync();
}
catch (Exception ex)
{
    var message = $"Error starting the reaction: {ex.Message}";
    Reaction<QueryConfig>.TerminateWithError(message);
}