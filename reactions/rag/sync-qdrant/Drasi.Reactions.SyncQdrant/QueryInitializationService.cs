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

using Drasi.Reaction.SDK.Services;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OpenAI.Embeddings;
using Drasi.Reaction.SDK.Models.ViewService;

namespace Drasi.Reactions.SyncQdrant;

public interface IQueryInitializationService
{
    public Task InitializeQueriesAsync(CancellationToken cancellationToken);
}

public class QueryInitializationService : IQueryInitializationService
{
    private readonly ILogger<QueryInitializationService> _logger;
    private readonly QdrantClient _qdrantClient;
    private readonly IResultViewClient _resultViewClient;
    private readonly IQuerySyncPointManager _querySyncPointManager;
    private readonly IQueryConfigService _queryConfigService;
    private readonly EmbeddingClient _embeddingClient;
    private readonly IExtendedManagementClient _managementClient;
    private readonly IErrorStateHandler _errorStateHandler;
    private readonly ReactionConfig _reactionConfig;
    public const int DefaultWaitForQueryReadySeconds = 300; // 5 minutes

    // Define field names expected in DataData for constructing the embedding text
    private const string FreezerIdFieldName = "id";
    private const string FreezerTemperatureFieldName = "temperature";

    public QueryInitializationService(
        ILogger<QueryInitializationService> logger,
        QdrantClient qdrantClient,
        IResultViewClient resultViewClient,
        IExtendedManagementClient managementClient,
        IErrorStateHandler errorStateHandler,
        IQuerySyncPointManager querySyncPointManager,
        IQueryConfigService queryConfigService,
        ReactionConfig reactionConfig,
        EmbeddingClient embeddingClient)
    {
        _logger = logger;
        _qdrantClient = qdrantClient;
        _resultViewClient = resultViewClient;
        _querySyncPointManager = querySyncPointManager;
        _queryConfigService = queryConfigService;
        _embeddingClient = embeddingClient;
        _managementClient = managementClient;
        _reactionConfig = reactionConfig;
        _errorStateHandler = errorStateHandler;
    }

    public async Task InitializeQueriesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initializing the queries...");
        var queryNames = _queryConfigService.GetQueryNames();
        if (queryNames.Count == 0)
        {
            _logger.LogWarning("No Queries configured.");
            return;
        }

        HashSet<string> existingCollections;
        try
        {
            _logger.LogDebug("Fetching names of existing collections in Qdrant...");
            existingCollections = [.. await _qdrantClient.ListCollectionsAsync(cancellationToken)];
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error while fetching existing collections: {ex.Message}";
            _logger.LogError(ex, errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw;
        }

        foreach (var queryName in queryNames)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var queryConfig = _queryConfigService.GetQueryConfig<QueryConfig>(queryName);
            if (queryConfig == null)
            {
                _logger.LogError("Query configuration for {QueryName} not found.", queryName);
                continue;
            }

            var collectionName = queryConfig.QdrantCollectionName;
            var keyFieldName = queryConfig.KeyFieldName;
            await EnsureCollectionExistsAsync(collectionName, existingCollections, cancellationToken);

            if (await _querySyncPointManager.TryLoadSyncPointAsync(queryName, collectionName, cancellationToken))
            {
                _logger.LogInformation("Sync point for query {QueryName} loaded successfully.", queryName);
            }
            else
            {
                await BootstrapCollectionForQuery(queryName, collectionName, keyFieldName, cancellationToken);
                _logger.LogInformation("Successfully bootstrapped collection {collectionName} for query {queryName}.",
                    collectionName, queryName);
            }
        }
    }

    private async Task BootstrapCollectionForQuery(string queryName, string collectionName, string keyFieldName, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Sync point for query {QueryName} not found. Starting full sync...", queryName);
        long querySyncPoint = -1;
        try
        {
            if (await _managementClient.WaitForQueryReadyAsync(queryName, DefaultWaitForQueryReadySeconds, cancellationToken))
            {
                querySyncPoint = await LoadCurrentResultsForQueryAsync(queryName, collectionName, keyFieldName, cancellationToken);
            }
            else
            {
                var errorMessage = $"Query {queryName} did not become ready within the specified time.";
                _logger.LogError(errorMessage);
                _errorStateHandler.Terminate(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error while waiting for query {queryName} to be ready: {ex.Message}";
            _logger.LogError(ex, errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw;
        }

        if (await _querySyncPointManager.TryUpdateSyncPointAsync(queryName, collectionName, querySyncPoint, cancellationToken))
        {
            _logger.LogInformation("Sync point for query {QueryName} updated successfully.", queryName);
        }
        else
        {
            var errorMessage = $"Failed to update sync point for query {queryName}.";
            _logger.LogError(errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }
    }

    private async Task<long> LoadCurrentResultsForQueryAsync(string queryName, string collectionName, string keyFieldName, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching initial data for query {QueryId} from ResultViewClient...", queryName);
        long querySyncPoint = -1;
        var stream = _resultViewClient.GetCurrentResult(queryName, cancellationToken);
        var pointsToUpsert = new List<PointStruct>();

        await using (var streamEnumerator = stream.GetAsyncEnumerator(cancellationToken))
        {
            querySyncPoint = await GetQuerySyncPointFromHeaderAsync(streamEnumerator, queryName);
            pointsToUpsert = await BuildListOfPointsToUpsertAsync(streamEnumerator, queryName, keyFieldName, cancellationToken);
        }

        if (pointsToUpsert.Count > 0)
        {
            _logger.LogDebug("Upserting {Count} points to Qdrant collection {CollectionName}...", pointsToUpsert.Count, collectionName);
            await _qdrantClient.UpsertAsync(collectionName, pointsToUpsert, cancellationToken: cancellationToken);
            _logger.LogInformation("Successfully upserted {Count} points to Qdrant collection {CollectionName}.", pointsToUpsert.Count, collectionName);
        }
        else
        {
            _logger.LogWarning("No points to upsert for query {QueryId}.", queryName);
        }

        return querySyncPoint;
    }

    private async Task<long> GetQuerySyncPointFromHeaderAsync(IAsyncEnumerator<ViewItem> streamEnumerator, string queryName)
    {
        try
        {
            if (await streamEnumerator.MoveNextAsync())
            {
                var firstItem = streamEnumerator.Current;
                if (firstItem?.Header == null)
                {
                    var errorMessage = $"Header in result stream is null for query {queryName}. Aborting initial sync.";
                    _logger.LogError(errorMessage);
                    _errorStateHandler.Terminate(errorMessage);
                    throw new InvalidProgramException(errorMessage);
                }

                return firstItem.Header.Sequence;
            }
            else
            {
                var errorMessage = $"No header returned in result stream for query {queryName}. Aborting initial sync.";
                _logger.LogError(errorMessage);
                _errorStateHandler.Terminate(errorMessage);
                throw new InvalidProgramException(errorMessage);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error while fetching result stream header for query {queryName}.";
            _logger.LogError(ex, errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw;
        }
    }

    private async Task<List<PointStruct>> BuildListOfPointsToUpsertAsync(
        IAsyncEnumerator<ViewItem> streamEnumerator,
        string queryName,
        string keyFieldName,
        CancellationToken cancellationToken)
    {
        List<PointStruct> pointsToUpsert = new();
        try
        {
            while (await streamEnumerator.MoveNextAsync())
            {
                if (cancellationToken.IsCancellationRequested) return pointsToUpsert;

                var viewItem = streamEnumerator.Current;
                if (viewItem?.Data == null) continue;

                var point = await GenerateEmbeddingPointAsync(viewItem.Data, cancellationToken);
                if (point != null)
                {
                    pointsToUpsert.Add(point);
                }
            }

            return pointsToUpsert;
        }
        catch (Exception ex)
        {
            var errorMessage = $"Unexpected error while parsing result stream for query {queryName}.";
            _logger.LogError(ex, errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw;
        }
    }

    private async Task<PointStruct?> GenerateEmbeddingPointAsync(Dictionary<string, object> queryResultItem, CancellationToken cancellationToken)
    {
        // Extract "id" and "temperature" for the embedding text
        if (!queryResultItem.TryGetValue(FreezerIdFieldName, out var idObject) || idObject == null)
        {
            throw new InvalidOperationException($"Missing or null '{FreezerIdFieldName}' in query result item.");
        }
        string freezerIdString = idObject.ToString() ?? string.Empty;

        if (!queryResultItem.TryGetValue(FreezerTemperatureFieldName, out var tempObject) || tempObject == null)
        {
            throw new InvalidOperationException($"Missing or null '{FreezerTemperatureFieldName}' in query result item.");
        }
        string temperatureString = tempObject.ToString() ?? string.Empty; // Convert to string, handle various numeric types

        // Construct the text for embedding
        var textToEmbed = $"Freezer ID: {freezerIdString} Last Reported Temperature: {temperatureString}°C";

        try
        {
            var embeddingsOptions = new EmbeddingGenerationOptions();
            var response = await _embeddingClient.GenerateEmbeddingAsync(textToEmbed, embeddingsOptions, cancellationToken);

            if (response?.Value == null)
            {
                _logger.LogWarning("OpenAI returned no embeddings for text: '{TextToEmbed}'. Skipping.", textToEmbed);
                return null;
            }
            var embedding = response.Value;
            var qdrantVector = embedding.ToFloats().ToArray();

            var payload = new Dictionary<string, Value>
            {
                ["freezer_id"] = freezerIdString,
                ["last_reported_temperature"] = temperatureString,
                ["text"] = textToEmbed
            };

            return new PointStruct
            {
                Id = new PointId { Num = ulong.Parse(freezerIdString) },
                Payload = { payload },
                Vectors = new Vectors { Vector = qdrantVector }
            };
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error generating embedding for text: '{textToEmbed}'.";
            _logger.LogError(ex, errorMessage);
            return null;
        }
    }

    private async Task EnsureCollectionExistsAsync(string collectionName, HashSet<string> existingCollections, CancellationToken cancellationToken)
    {
        try
        {
            if (!existingCollections.Contains(collectionName))
            {
                _logger.LogInformation("Collection {CollectionName} does not exist. Creating...", collectionName);
                await _qdrantClient.CreateCollectionAsync(
                    collectionName,
                    new VectorParams { Size = _reactionConfig.ModelVectorDimensions, Distance = _reactionConfig.QdrantDistanceMetric },
                    cancellationToken: cancellationToken
                );
                _logger.LogInformation("Collection {CollectionName} created successfully.", collectionName);
            }
            else
            {
                _logger.LogInformation("Collection {CollectionName} already exists.", collectionName);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"Error while ensuring that collection {collectionName} exist: {ex.Message}";
            _logger.LogError(ex, errorMessage);
            _errorStateHandler.Terminate(errorMessage);
            throw;
        }
    }
}