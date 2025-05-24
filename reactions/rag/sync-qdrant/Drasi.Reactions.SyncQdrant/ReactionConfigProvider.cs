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

using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;

namespace Drasi.Reactions.SyncQdrant;

public interface IReactionConfigProvider
{
    ReactionConfig GetValidatedReactionConfig();
}

public class ReactionConfigProvider : IReactionConfigProvider
{
    private readonly IConfiguration _configuration;
    private readonly IErrorStateHandler _errorStateHandler;
    private readonly ILogger<ReactionConfigProvider> _logger;

    public ReactionConfigProvider(
        IConfiguration configuration,
        IErrorStateHandler errorStateHandler,
        ILogger<ReactionConfigProvider> logger)
    {
        _configuration = configuration;
        _errorStateHandler = errorStateHandler;
        _logger = logger;
    }

    public ReactionConfig GetValidatedReactionConfig()
    {
        _logger.LogInformation("Loading and validating Reaction Configuration...");

        try
        {
            string? qdrantHost = _configuration.GetValue<string>("qdrantHost");
            if (string.IsNullOrWhiteSpace(qdrantHost))
            {
                _logger.LogError("Configuration error: `qdrantHost` is missing or empty.");
                _errorStateHandler.Terminate("Configuration error: `qdrantHost` is missing or empty.");
                throw new InvalidOperationException("`qdrantHost` configuration is missing. Termination initiated.");
            }

            int qdrantPort = _configuration.GetValue<int>("qdrantPort", 6334);
            if (qdrantPort <= 0 || qdrantPort > 65535)
            {
                _errorStateHandler.Terminate("Configuration error: `qdrantPort` is invalid. Must be between 1 and 65535.");
                throw new InvalidOperationException("`qdrantPort` configuration is invalid. Termination initiated.");
            }

            bool qdrantHttps = _configuration.GetValue<bool>("qdrantHttps", false);

            string qdrantSyncMetadataPointIdString = _configuration.GetValue<string>("qdrantSyncMetadataPointId", "00000000-0000-0000-0000-000000000000");
            if (!Guid.TryParse(qdrantSyncMetadataPointIdString, out Guid _))
            {
                _logger.LogError("Configuration error: `qdrantSyncMetadataPointId` is not a valid GUID.");
                _errorStateHandler.Terminate("Configuration error: `qdrantSyncMetadataPointId` is not a valid GUID.");
                throw new InvalidOperationException("`qdrantSyncMetadataPointId` configuration is not a valid GUID. Termination initiated.");
            }
            var qdrantSyncMetadataPointId = new PointId { Uuid = qdrantSyncMetadataPointIdString };

            var qdrantDistanceMetricString = _configuration.GetValue<string>("qdrantDistanceMetric", "Cosine");
            if (!Enum.TryParse<Distance>(qdrantDistanceMetricString, true, out var qdrantDistanceMetric))
            {
                _logger.LogError("Configuration error: `qdrantDistanceMetric` is not a valid enum value.");
                _errorStateHandler.Terminate("Configuration error: `qdrantDistanceMetric` is not a valid enum value.");
                throw new InvalidOperationException("`qdrantDistanceMetric` configuration is not a valid enum value. Termination initiated.");
            }

            string? azureOpenAIEndpointString = _configuration.GetValue<string>("azureOpenAIEndpoint");
            if (string.IsNullOrWhiteSpace(azureOpenAIEndpointString))
            {
                _logger.LogError("Configuration error: `azureOpenAIEndpoint` is missing or empty.");
                _errorStateHandler.Terminate("Configuration error: `azureOpenAIEndpoint` is missing or empty.");
                throw new InvalidOperationException("`azureOpenAIEndpoint` configuration is missing. Termination initiated.");
            }

            if (!Uri.TryCreate(azureOpenAIEndpointString, UriKind.Absolute, out var azureOpenAIEndpoint))
            {
                _logger.LogError("Configuration error: `azureOpenAIEndpoint` is not a valid URI.");
                _errorStateHandler.Terminate("Configuration error: `azureOpenAIEndpoint` is not a valid URI.");
                throw new InvalidOperationException("`azureOpenAIEndpoint` configuration is not a valid URI. Termination initiated.");
            }

            string? azureOpenAIKeyString = _configuration.GetValue<string>("azureOpenAIKey");
            if (string.IsNullOrWhiteSpace(azureOpenAIKeyString))
            {
                _logger.LogError("Configuration error: `azureOpenAIKey` is missing or empty.");
                _errorStateHandler.Terminate("Configuration error: `azureOpenAIKey` is missing or empty.");
                throw new InvalidOperationException("`azureOpenAIKey` configuration is missing. Termination initiated.");
            }
            var azureOpenAIKey = new AzureKeyCredential(azureOpenAIKeyString);

            string? embeddingModelName = _configuration.GetValue<string>("embeddingModelName");
            if (string.IsNullOrWhiteSpace(embeddingModelName))
            {
                _logger.LogError("Configuration error: `embeddingModelName` is missing or empty.");
                _errorStateHandler.Terminate("Configuration error: `embeddingModelName` is missing or empty.");
                throw new InvalidOperationException("`embeddingModelName` configuration is missing. Termination initiated.");
            }

            var modelVectorDimensions = _configuration.GetValue<ulong>("modelVectorDimensions", 3072); // default for text-embedding-3-large

            _logger.LogInformation("Reaction Configuration loaded successfully.");
            _logger.LogInformation("[DEBUG] Qdrant Host: {Host}, Port: {Port}, Https: {Https}",
                qdrantHost, qdrantPort, qdrantHttps);
            _logger.LogInformation("[DEBUG] Azure OpenAI Endpoint: {Endpoint}, Key: {Key}, Embedding Model Name: {ModelName}",
                azureOpenAIEndpoint, azureOpenAIKeyString, embeddingModelName);
            return new ReactionConfig
            {
                QdrantHost = qdrantHost,
                QdrantPort = qdrantPort,
                QdrantHttps = qdrantHttps,
                QdrantSyncMetadataPointId = qdrantSyncMetadataPointId,
                QdrantDistanceMetric = qdrantDistanceMetric,
                AzureOpenAIEndpoint = azureOpenAIEndpoint,
                AzureOpenAIKey = azureOpenAIKey,
                EmbeddingModelName = embeddingModelName,
                ModelVectorDimensions = modelVectorDimensions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading or validating Reaction Configuration.");
            _errorStateHandler.Terminate($"Configuration error: {ex.Message}");
            throw;
        }
    }
}