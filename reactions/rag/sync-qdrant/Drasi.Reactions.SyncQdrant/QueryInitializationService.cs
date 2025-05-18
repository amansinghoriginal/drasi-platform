using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.SyncQdrant;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Azure.AI.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Drasi.Reaction.SDK.Models.ViewService;
using System.Text.Json;
using Grpc.Core;
using Google.Protobuf.Collections;
using OpenAI.Embeddings;
using Azure;

namespace Drasi.Reactions.SyncQdrant
{
    public class QueryInitializationService : IHostedService
    {
        private readonly ILogger<QueryInitializationService> _logger;
        private readonly QdrantClient _qdrantClient;
        private readonly IResultViewClient _resultViewClient;
        private readonly IQuerySyncStateManager _querySyncStateManager;
        private readonly IQueryConfigService _queryConfigService;
        private readonly AzureOpenAIClient _openAIClient;
        private readonly EmbeddingClient _embeddingClient;

        private const string QdrantCollectionName = "drasi-poc-collection";
        private const string QdrantSyncStatePointId = "00000000-0000-0000-0000-000000000000";
        private const uint VectorDimensions = 3072; // For text-embedding-3-large
        private static readonly Distance QdrantDistanceMetric = Distance.Cosine;
        private const string OpenAIEmbeddingDeploymentName = "text-embedding-3-large";
        private const int QdrantUpsertBatchSize = 100;

        // Define field names expected in DataData for constructing the embedding text
        private const string FreezerIdFieldName = "id";
        private const string FreezerTemperatureFieldName = "temperature";


        public QueryInitializationService(
            ILogger<QueryInitializationService> logger,
            QdrantClient qdrantClient,
            IResultViewClient resultViewClient,
            IQuerySyncStateManager querySyncStateManager,
            IQueryConfigService queryConfigService,
            AzureOpenAIClient openAIClient,
            IConfiguration configuration)
        {
            _logger = logger;
            _qdrantClient = qdrantClient;
            _resultViewClient = resultViewClient;
            _querySyncStateManager = querySyncStateManager;
            _queryConfigService = queryConfigService;
            _openAIClient = openAIClient;
            _embeddingClient = openAIClient.GetEmbeddingClient("text-embedding-3-large");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Query Initialization Service starting.");

            var queryNames = _queryConfigService.GetQueryNames();
            if (!queryNames.Any())
            {
                _logger.LogWarning("No query configurations found. Initialization service will not process any queries.");
                return;
            }

            var queryIdToProcess = queryNames.First();
            _logger.LogInformation("Attempting to initialize query: {QueryId}", queryIdToProcess);

            try
            {
                await EnsureCollectionExistsAsync(cancellationToken);
                var syncStatePoint = await GetPointOrDefaultAsync(QdrantCollectionName, QdrantSyncStatePointId, cancellationToken);

                if (syncStatePoint != null &&
                    syncStatePoint.Payload.TryGetValue("last_processed_sequence", out var sequencePayloadValue) &&
                    sequencePayloadValue.KindCase == Value.KindOneofCase.IntegerValue)
                {
                    long lastProcessedSequence = sequencePayloadValue.IntegerValue;
                    _logger.LogInformation("Found existing sync state for query {QueryId}. Last processed sequence: {Sequence}", queryIdToProcess, lastProcessedSequence);
                    _querySyncStateManager.SetInitialSequence(queryIdToProcess, lastProcessedSequence);
                }
                else
                {
                    if (syncStatePoint == null)
                        _logger.LogInformation("No sync state point found for query {QueryId} in Qdrant.", queryIdToProcess);
                    else
                        _logger.LogWarning("Sync state point found for query {QueryId}, but 'last_processed_sequence' payload was missing or not a valid long. Proceeding with full sync.", queryIdToProcess);
                    
                    await PerformFullSyncAsync(queryIdToProcess, cancellationToken);
                }
                _logger.LogInformation("Initialization completed for query: {QueryId}", queryIdToProcess);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during query initialization for {QueryId}. The reaction might not function correctly.", queryIdToProcess);
            }
            _logger.LogInformation("Query Initialization Service startup sequence finished.");
        }

        private async Task EnsureCollectionExistsAsync(CancellationToken cancellationToken)
        {
            try
            {
                var collections = await _qdrantClient.ListCollectionsAsync(cancellationToken);
                if (!collections.Contains(QdrantCollectionName))
                {
                    _logger.LogInformation("Collection {CollectionName} does not exist. Creating it with Dimensions: {Dimensions}, Metric: {Metric}.",
                        QdrantCollectionName, VectorDimensions, QdrantDistanceMetric);
                    await _qdrantClient.CreateCollectionAsync(
                        collectionName: QdrantCollectionName,
                        vectorsConfig: new VectorParams { Size = VectorDimensions, Distance = QdrantDistanceMetric },
                        cancellationToken: cancellationToken
                    );
                    _logger.LogInformation("Collection {CollectionName} created successfully.", QdrantCollectionName);
                }
                else
                {
                    _logger.LogInformation("Collection {CollectionName} already exists.", QdrantCollectionName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure Qdrant collection {CollectionName} exists. This is critical for operation.", QdrantCollectionName);
                throw;
            }
        }

        private async Task<RetrievedPoint?> GetPointOrDefaultAsync(string collectionName, string pointIdValue, CancellationToken cancellationToken)
        {
            try
            {
                var pointId = new PointId { Uuid = pointIdValue };
                var retrievedPoints = await _qdrantClient.RetrieveAsync(
                    collectionName,
                    ids: new[] { pointId },
                    withPayload: true,
                    withVectors: false,
                    cancellationToken: cancellationToken
                );
                return retrievedPoints.FirstOrDefault();
            }
            catch (RpcException ex)
            {
                // Qdrant returns a "NotFound" gRPC status code if the collection or point does not exist.
                if (ex.StatusCode == StatusCode.NotFound)
                {
                    _logger.LogInformation("Point {PointId} not found in collection {CollectionName}.", pointIdValue, collectionName);
                    return null;
                }
                _logger.LogError(ex, "A gRPC error occurred while trying to get point {PointId} from collection {CollectionName}.", pointIdValue, collectionName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while trying to get point {PointId} from collection {CollectionName}.", pointIdValue, collectionName);
                throw;
            }
        }
        
        private async Task PerformFullSyncAsync(string queryName, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting full sync for query {QueryName}.", queryName);
            long queryInitializationSequenceNumber = -1;
            var stream = _resultViewClient.GetCurrentResult(queryName, cancellationToken);
            var pointsToUpsert = new List<PointStruct>();

            await using (var streamEnumerator = stream.GetAsyncEnumerator(cancellationToken))
            {
                // 1. Fetch Header
                try
                {
                    if (await streamEnumerator.MoveNextAsync())
                    {
                        var firstItem = streamEnumerator.Current;
                        if (firstItem?.Header == null)
                        {
                            var errorMessage = $"Header is null for query {queryName}. Aborting initial sync.";
                            _logger.LogError(errorMessage);
                            throw new InvalidOperationException(errorMessage);
                        }

                        queryInitializationSequenceNumber = firstItem.Header.Sequence;
                    }
                    else
                    {
                        var errorMessage = $"No data found for query {queryName}. Aborting initial sync.";
                        _logger.LogError(errorMessage);
                        throw new InvalidOperationException(errorMessage);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in fetching header from View Service for query {queryName}", ex);
                    throw;
                }

                // 2. Process Data Items
                try
                {
                    while (await streamEnumerator.MoveNextAsync())
                    {
                        var viewItem = streamEnumerator.Current;
                        if (viewItem?.Data == null) continue;

                        // Extract "id" and "temperature" for the embedding text
                        if (!viewItem.Data.TryGetValue(FreezerIdFieldName, out var idObject) || idObject == null)
                        {
                            throw new InvalidOperationException($"Missing or null '{FreezerIdFieldName}' in Data for item {viewItem}.");
                        }
                        string freezerIdString = idObject.ToString() ?? string.Empty;

                        if (!viewItem.Data.TryGetValue(FreezerTemperatureFieldName, out var tempObject) || tempObject == null)
                        {
                            throw new InvalidOperationException($"Missing or null '{FreezerTemperatureFieldName}' in Data for item {viewItem}.");
                        }
                        string temperatureString = tempObject.ToString() ?? string.Empty; // Convert to string, handle various numeric types

                        // Construct the text for embedding
                        string textToEmbed = $"Freezer ID: {freezerIdString} Last Reported Temperature: {temperatureString}°C";

                        // Correct usage of EmbeddingGenerationOptions and GenerateEmbeddingAsync
                        var embeddingsOptions = new EmbeddingGenerationOptions();
                        System.ClientModel.ClientResult<OpenAIEmbedding> response = await _embeddingClient.GenerateEmbeddingAsync(textToEmbed, embeddingsOptions, cancellationToken);

                        if (response?.Value == null)
                        {
                            _logger.LogWarning("OpenAI returned no embeddings for item {Data}, text: '{TextToEmbed}'. Skipping.", viewItem.Data, textToEmbed);
                            continue;
                        }

                        var embedding = response.Value;
                        var qdrantVector = embedding.ToFloats().ToArray();

                        var payload = new Dictionary<string, Value>
                        {
                            ["freezer_id"] = freezerIdString,
                            ["last_reported_temperature"] = temperatureString,
                            ["text"] = textToEmbed
                        };

                        var pointStruct = new PointStruct
                        {
                            Id = new PointId { Num = ulong.Parse(freezerIdString) },
                            Payload = { payload },
                            Vectors = new Vectors { Vector = qdrantVector }
                        };

                        pointsToUpsert.Add(pointStruct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing data items for query {QueryName}.", queryName);
                    throw;
                }
            }

            // 3. Upsert points to Qdrant
            if (pointsToUpsert.Count > 0)
            {
                pointsToUpsert.Add(new PointStruct
                {
                    Id = new PointId { Uuid = QdrantSyncStatePointId },
                    Payload = { { "last_processed_sequence", queryInitializationSequenceNumber } },
                    Vectors = new Vectors { Vector = new float[VectorDimensions] } // Placeholder vector
                });
                _logger.LogInformation("Upserting {Count} points to Qdrant for query {QueryName}.", pointsToUpsert.Count, queryName);
                await _qdrantClient.UpsertAsync(QdrantCollectionName, pointsToUpsert, cancellationToken: cancellationToken);
            }
            else
            {
                _logger.LogWarning("No points to upsert for query {QueryName}.", queryName);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Query Initialization Service stopping.");
            return Task.CompletedTask;
        }
    }
}