using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.SyncQdrant;

/// <summary>
/// Manages the synchronization state (initialized status and sequence number) for each query.
/// This service should be registered as a singleton.
/// </summary>
public interface IQuerySyncStateManager
{
    /// <summary>
    /// Sets the initial sequence number for a query, typically after a full sync.
    /// </summary>
    /// <param name="queryId">The ID of the query.</param>
    /// <param name="sequenceNumber">The initial sequence number.</param>
    void SetInitialSequence(string queryId, long sequenceNumber);

    /// <summary>
    /// Updates the last processed sequence number for a query.
    /// </summary>
    /// <param name="queryId">The ID of the query.</param>
    /// <param name="sequenceNumber">The sequence number of the last successfully processed event.</param>
    void UpdateSequence(string queryId, long sequenceNumber);

    /// <summary>
    /// Gets the last processed sequence number for a query.
    /// </summary>
    /// <param name="queryId">The ID of the query.</param>
    /// <param name="sequenceNumber">The last processed sequence number if the query is initialized, otherwise -1.</param>
    /// <returns>True if the query has an initialized sequence number, false otherwise.</returns>
    bool TryGetLastProcessedSequence(string queryId, out long sequenceNumber);

    /// <summary>
    /// Checks if a query is considered initialized (i.e., an initial sequence number has been set).
    /// </summary>
    /// <param name="queryId">The ID of the query.</param>
    /// <returns>True if initialized, false otherwise.</returns>
    bool IsInitialized(string queryId);
}

/// <summary>
/// Represents the synchronization state for a single query.
/// </summary>
internal class QuerySyncState
{
    /// <summary>
    /// Indicates if the point to resume processing changes for this query been determined.
    /// </summary>
    public bool IsInitialized { get; set; } = false;

    /// <summary>
    /// The last sequence number of events until which the the Qdrant collection is
    ///     considered to be synced for this query.
    /// Defaults to -1, indicating no sequence has been processed yet.
    /// </summary>
    public long LastProcessedSequence { get; set; } = -1;
}

/// <summary>
/// Implementation of IQuerySyncStateManager that tracks query initialization state and sequence numbers.
/// </summary>
public class QuerySyncStateManager : IQuerySyncStateManager
{
    private readonly ConcurrentDictionary<string, QuerySyncState> _queryStates = new();
    private readonly ILogger<QuerySyncStateManager> _logger;

    public QuerySyncStateManager(ILogger<QuerySyncStateManager> logger)
    {
        _logger = logger;
    }

    public void SetInitialSequence(string queryId, long sequenceNumber)
    {
        var state = _queryStates.GetOrAdd(queryId, _ => new QuerySyncState());
        state.LastProcessedSequence = sequenceNumber;
        state.IsInitialized = true;
        _logger.LogDebug("Initial sequence for query {QueryId} set to {SequenceNumber}", queryId, sequenceNumber);
    }

    public void UpdateSequence(string queryId, long sequenceNumber)
    {
        if (_queryStates.TryGetValue(queryId, out var state))
        {
            if (sequenceNumber > state.LastProcessedSequence)
            {
                state.LastProcessedSequence = sequenceNumber;
                if (!state.IsInitialized)
                {
                    state.IsInitialized = true;
                    _logger.LogWarning("Query {QueryId} was updated with sequence {SequenceNumber} without being initialized. Marked Initialized now.", queryId, sequenceNumber);
                }

                _logger.LogDebug("Sequence for query {QueryId} updated to {SequenceNumber}", queryId, sequenceNumber);
            }
            else if (sequenceNumber < state.LastProcessedSequence)
            {
                _logger.LogWarning("Ignoring attempt to update sequence for query {QueryId} to older {NewSequence} because current is {CurrentSequence}.",
                    queryId, sequenceNumber, state.LastProcessedSequence);
            }
        }
        else
        {
            _logger.LogWarning("Attempted to update sequence for non-tracked query {QueryId}", queryId);
        }
    }

    public bool TryGetLastProcessedSequence(string queryId, out long sequenceNumber)
    {
        sequenceNumber = -1;
        if (_queryStates.TryGetValue(queryId, out var state) && state.IsInitialized)
        {
            sequenceNumber = state.LastProcessedSequence;
            return true;
        }

        return false;
    }

    public bool IsInitialized(string queryId)
    {
        return _queryStates.TryGetValue(queryId, out var state) && state.IsInitialized;
    }
}