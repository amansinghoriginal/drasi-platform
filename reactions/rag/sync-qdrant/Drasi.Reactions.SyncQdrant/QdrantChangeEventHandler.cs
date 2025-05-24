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
using Drasi.Reaction.SDK.Models.QueryOutput;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OpenAI.Embeddings;
using System.Collections.Concurrent;
using Grpc.Core;

namespace Drasi.Reactions.SyncQdrant;

public class QdrantChangeEventHandler : IChangeEventHandler<QueryConfig>
{
    private readonly ILogger<QdrantChangeEventHandler> _logger;
    private readonly QdrantClient _qdrantClient;
    private readonly IQuerySyncPointManager _querySyncPointManager;
    private readonly EmbeddingClient _embeddingClient;
    
    // Define field names expected in DataData for constructing the embedding text
    private const string FreezerIdFieldName = "id";
    private const string FreezerTemperatureFieldName = "temperature";

    public QdrantChangeEventHandler(
        ILogger<QdrantChangeEventHandler> logger,
        IQuerySyncPointManager querySyncPointManager,
        QdrantClient qdrantClient,
        EmbeddingClient embeddingClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _qdrantClient = qdrantClient ?? throw new ArgumentNullException(nameof(qdrantClient));
        _embeddingClient = embeddingClient ?? throw new ArgumentNullException(nameof(embeddingClient));
        _querySyncPointManager = querySyncPointManager ?? throw new ArgumentNullException(nameof(querySyncPointManager));
    }

    public async Task HandleChange(ChangeEvent evt, QueryConfig? config)
    {
        _logger.LogDebug("Received change event for query {QueryId} with sequence {Sequence}. Added: {AddedCount}, Updated: {UpdatedCount}, Deleted: {DeletedCount}",
            evt.QueryId, evt.Sequence, evt.AddedResults?.Count() ?? 0, evt.UpdatedResults?.Count() ?? 0, evt.DeletedResults?.Count() ?? 0);

        var queryName = evt.QueryId;
        var queryConfig = config ?? throw new ArgumentNullException(nameof(config));
        var collectionName = queryConfig.QdrantCollectionName;
        var keyFieldName = queryConfig.KeyFieldName;

        var syncPoint = _querySyncPointManager.GetSyncPointForQuery(evt.QueryId);
        if (syncPoint == null)
        {
            var message = $"Received Change Event for Query {evt.QueryId} which was not yet initialized.";
            _logger.LogWarning(message);
            throw new InvalidOperationException(message); // Pub/sub should deliver this event again.
        }

        if (evt.Sequence < syncPoint)
        {
            _logger.LogInformation("Skipping change event {Sequence} for query {QueryId} as it is older than current sync point {SyncPoint}.",
                evt.Sequence, evt.QueryId, syncPoint);
            return;
        }

        var pointsToUpsert = new List<PointStruct>();
        var pointsToDelete = new List<ulong>();
        var exceptions = new ConcurrentBag<Exception>();

        // Prepare Adds/Updates for Bulk Save
        var allUpserts = (evt.AddedResults ?? Enumerable.Empty<Dictionary<string, object>>())
            .Concat((evt.UpdatedResults ?? Enumerable.Empty<UpdatedResultElement>()).Select(u => u.After));

        foreach (var itemData in allUpserts)
        {
            if (itemData == null)
            {
                continue;
            }

            var itemKey = itemData[keyFieldName]?.ToString()
                ?? throw new ArgumentNullException($"Key field '{keyFieldName}' was not found in query result.");

            var pointStruct = await GenerateEmbeddingPointAsync(keyFieldName, itemData, CancellationToken.None);
            if (pointStruct != null)
            {
                pointsToUpsert.Add(pointStruct);
            }
        }

        // Prepare Deletes
        foreach (var deletedItem in evt.DeletedResults ?? Enumerable.Empty<Dictionary<string, object>>())
        {
            if (deletedItem == null)
            {
                continue;
            }
            
            var keyToDelete = deletedItem[keyFieldName]?.ToString()
                ?? throw new ArgumentNullException($"Key field '{keyFieldName}' for the entry being deleted is null.");
            if (!ulong.TryParse(keyToDelete, out var keyValue))
                throw new InvalidOperationException($"Key field '{keyFieldName}' for the entry being deleted is not a valid number.");

            pointsToDelete.Add(keyValue);
        }

        // Execute Bulk Upsert for Adds/Updates
        if (pointsToUpsert.Count > 0)
        {
            try
            {
                _logger.LogDebug("Attempting to upsert {Count} items to Qdrant collection '{CollectionName}' for query {QueryId}'s event {Sequence}",
                    pointsToUpsert.Count, collectionName, queryName, evt.Sequence);
                
                var upsertResponse = await _qdrantClient.UpsertAsync(collectionName, pointsToUpsert);
                if (upsertResponse.Status != UpdateStatus.Completed && upsertResponse.Status != UpdateStatus.Acknowledged)
                {
                    var message = $"Qdrant bulk save operation failed during event {evt.Sequence} for query {queryName}";
                    _logger.LogError(message);
                    throw new InvalidOperationException(message);
                }

                _logger.LogDebug("Successfully upserted {Count} items to Qdrant collection '{CollectionName}' for query {QueryId}'s event {Sequence}",
                    pointsToUpsert.Count, collectionName, queryName, evt.Sequence);
            }
            catch (RpcException ex)
            {
                var message = $"Qdrant upsert operation failed during event {evt.Sequence} for query {queryName}";
                _logger.LogError(ex, message);
                exceptions.Add(ex);
            }
            catch (Exception ex)
            {
                var message = $"Unexpected error during upsert operation for event {evt.Sequence} for query {queryName}";
                _logger.LogError(ex, message);
                exceptions.Add(ex);
            }
        }

        if (pointsToDelete.Count > 0)
        {
            try
            {
                _logger.LogDebug("Attempting to delete {Count} items from Qdrant collection '{CollectionName}' for query {QueryId}'s event {Sequence}",
                    pointsToDelete.Count, collectionName, queryName, evt.Sequence);
                
                var deleteResponse = await _qdrantClient.DeleteAsync(collectionName, pointsToDelete);
                if (deleteResponse.Status != UpdateStatus.Completed && deleteResponse.Status != UpdateStatus.Acknowledged)
                {
                    var message = $"Qdrant delete operation failed during event {evt.Sequence} for query {queryName}";
                    _logger.LogError(message);
                    throw new InvalidOperationException(message);
                }

                _logger.LogDebug("Successfully deleted {Count} items from Qdrant collection '{CollectionName}' for query {QueryId}'s event {Sequence}",
                    pointsToDelete.Count, collectionName, queryName, evt.Sequence);
            }
            catch (RpcException ex)
            {
                var message = $"Qdrant delete operation failed during event {evt.Sequence} for query {queryName}";
                _logger.LogError(ex, message);
                exceptions.Add(ex);
            }
            catch (Exception ex)
            {
                var message = $"Unexpected error during delete operation for event {evt.Sequence} for query {queryName}";
                _logger.LogError(ex, message);
                exceptions.Add(ex);
            }
        }

        // If any operations failed, throw an aggregate exception
        if (exceptions.Count > 0)
        {
            throw new AggregateException($"Failed to fully process change event {evt.Sequence} for query {queryName}. See inner exceptions for details.", exceptions);
        }

        if (await _querySyncPointManager.TryUpdateSyncPointAsync(queryName, collectionName, evt.Sequence))
        {
            _logger.LogInformation("Successfully processed change-event sequence {Sequence} for query {QueryId}.", evt.Sequence, queryName);
        }
        else
        {
            // Throw exception - this will cause the event to be re-delivered
            _logger.LogWarning("Failed to update sync point for query {QueryId} after processing change event {Sequence}.", queryName, evt.Sequence);
            throw new InvalidOperationException("Failed to update sync point after processing change event.");
        }
    }

    private async Task<PointStruct?> GenerateEmbeddingPointAsync(string keyFieldName, Dictionary<string, object> queryResultItem, CancellationToken cancellationToken)
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

        var keyFieldValueString = queryResultItem[keyFieldName]?.ToString();
        if (string.IsNullOrEmpty(keyFieldValueString) || !ulong.TryParse(keyFieldValueString, out var keyFieldValue))
        {
            throw new InvalidOperationException($"Key field '{keyFieldValueString}' is null or empty, or not a number.");
        }

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
                Id = new PointId { Num = keyFieldValue },
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
}