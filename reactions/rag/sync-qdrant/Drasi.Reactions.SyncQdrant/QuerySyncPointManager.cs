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

using System.Collections.Concurrent;
using Dapr;
using Dapr.Client;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Drasi.Reactions.SyncQdrant;
/// <summary>
/// Manages the synchronization points (sequence numbers) for Drasi queries.
/// This includes loading them from and persisting them to a metadata point in the
/// query's Qdrant collection, as well as maintaining an in-memory cache for quick access.
/// This service should be registered as a singleton.
/// </summary>
public interface IQuerySyncPointManager
{
    /// <summary>
    /// Attempts to load the sync point from Qdrant.
    /// </summary>
    /// <param name="queryId">The unique identifier for the query.</param>
    /// <param name="collectionName">The Qdrant collection name where the query's metadata is stored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is a boolean
    /// indicating whether the sync point was successfully loaded.
    /// </returns>
    Task<bool> TryLoadSyncPointAsync(string queryId, string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the sync point for a query if available.
    /// </summary>
    /// <param name="queryId">The unique identifier for the query.</param>
    /// <returns>
    /// The sync point for the query if available, otherwise null.
    /// </returns>
    long? GetSyncPointForQuery(string queryId);

    /// <summary>
    /// Updates the sync point in the Qdrant collection for a given query.
    /// </summary>
    /// <param name="queryId">The unique identifier for the query.</param>
    /// <param name="collectionName"> The Qdrant collection name where the query's metadata is stored.</param>
    /// <param name="sequenceNumber">The sequence number to be set as the new sync point.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result is a boolean
    /// indicating whether the sync point was successfully updated.
    /// </returns>
    Task<bool> TryUpdateSyncPointAsync(string queryId, string collectionName, long sequenceNumber, CancellationToken cancellationToken = default);
}

public class QuerySyncPointManager : IQuerySyncPointManager
{
    private readonly ConcurrentDictionary<string, long> _syncPoints;
    private readonly QdrantClient _qdrantClient;
    private readonly ILogger<QuerySyncPointManager> _logger;
    private readonly PointId _qdrantSyncStatePointId;
    private const uint VectorDimensions = 3072;

    internal const string SyncPointKey = "drasi";

    public QuerySyncPointManager(QdrantClient qdrantClient, ILogger<QuerySyncPointManager> logger, ReactionConfig reactionConfig)
    {
        _syncPoints = new();
        _qdrantClient = qdrantClient ?? throw new ArgumentNullException(nameof(qdrantClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _qdrantSyncStatePointId = reactionConfig?.QdrantSyncMetadataPointId ?? throw new ArgumentNullException(nameof(reactionConfig));
    }

    public long? GetSyncPointForQuery(string queryId)
    {
        if (_syncPoints.TryGetValue(queryId, out var sequenceNumber))
        {
            return sequenceNumber;
        }
        return null;
    }

    public async Task<bool> TryLoadSyncPointAsync(string queryId, string collectionName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Attempting to load sync point for query {QueryId} using PointId {SyncPointKey}...",
                queryId, _qdrantSyncStatePointId.Uuid);

            var retrievedPoint = (await _qdrantClient.RetrieveAsync(
                collectionName,
                new[] { _qdrantSyncStatePointId },
                withPayload: true,
                withVectors: false,
                cancellationToken: cancellationToken
            )).FirstOrDefault();

            if (retrievedPoint != null)
            {
                _logger.LogDebug("Sync point found for query {QueryId} using PointId {SyncPointKey}.",
                    queryId, _qdrantSyncStatePointId.Uuid);
                if (retrievedPoint.Payload.TryGetValue(SyncPointKey, out var syncPointValue) &&
                    syncPointValue.KindCase == Value.KindOneofCase.IntegerValue)
                {
                    long sequenceNumber = syncPointValue.IntegerValue;
                    _logger.LogInformation("Loaded sync point for query {QueryId}: {SequenceNumber}",
                        queryId, sequenceNumber);
                    _syncPoints[queryId] = sequenceNumber;
                    return true;
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode != Grpc.Core.StatusCode.NotFound)
        {
            _logger.LogError(ex, "Qdrant Error loading sync point for query {QueryId} using PointId {SyncPointKey}.",
                queryId, _qdrantSyncStatePointId.Uuid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading sync point for query {QueryId} using PointId {SyncPointKey}.",
                queryId, _qdrantSyncStatePointId.Uuid);
        }

        _logger.LogInformation("Sync point not found for query {QueryId} using PointId {SyncPointKey}.",
            queryId, _qdrantSyncStatePointId.Uuid);
        return false;
    }

    public async Task<bool> TryUpdateSyncPointAsync(string queryId, string collectionName, long sequenceNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Attempting to update sync point for query {QueryId} to {SequenceNumber}...",
                queryId, sequenceNumber);

            await _qdrantClient.UpsertAsync(collectionName,
                [
                    new PointStruct
                    {
                        Id = _qdrantSyncStatePointId,
                        Payload = { { SyncPointKey, sequenceNumber } },
                        Vectors = new Vectors { Vector = new float[VectorDimensions] }
                    }
                ],
                cancellationToken: cancellationToken);

            _logger.LogInformation("Updated sync point for query {QueryId} to {SequenceNumber}",
                queryId, sequenceNumber);
            _syncPoints[queryId] = sequenceNumber;
            return true;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Qdrant Error updating sync point for query {QueryId} to {SequenceNumber}.",
                queryId, sequenceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating sync point for query {QueryId} to {SequenceNumber}.",
                queryId, sequenceNumber);
        }

        _logger.LogWarning("Failed to update sync point for query {QueryId} to {SequenceNumber}.",
            queryId, sequenceNumber);
        return false;
    }
}