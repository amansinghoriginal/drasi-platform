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

using Drasi.Reactions.Mcp.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Drasi.Reactions.Mcp.Services;

/// <summary>
/// In-memory implementation of sync point manager
/// For production, consider persisting sync points to durable storage
/// </summary>
public class McpSyncPointManager : IMcpSyncPointManager
{
    private readonly ILogger<McpSyncPointManager> _logger;
    private readonly ConcurrentDictionary<string, long> _syncPoints = new();

    public McpSyncPointManager(ILogger<McpSyncPointManager> logger)
    {
        _logger = logger;
    }

    public long? GetSyncPoint(string queryId)
    {
        if (_syncPoints.TryGetValue(queryId, out var syncPoint))
        {
            return syncPoint;
        }
        
        _logger.LogDebug("No sync point found for query {QueryId}", queryId);
        return null;
    }

    public Task UpdateSyncPointAsync(string queryId, long sequence)
    {
        _syncPoints.AddOrUpdate(queryId, sequence, (key, oldValue) =>
        {
            if (sequence <= oldValue)
            {
                _logger.LogWarning("Attempted to update sync point for query {QueryId} to older sequence {NewSequence} (current: {CurrentSequence})", 
                    queryId, sequence, oldValue);
                return oldValue;
            }
            
            _logger.LogDebug("Updated sync point for query {QueryId} from {OldSequence} to {NewSequence}", 
                queryId, oldValue, sequence);
            return sequence;
        });
        
        return Task.CompletedTask;
    }

    public Task InitializeSyncPointAsync(string queryId, long sequence)
    {
        if (_syncPoints.TryAdd(queryId, sequence))
        {
            _logger.LogInformation("Initialized sync point for query {QueryId} to sequence {Sequence}", 
                queryId, sequence);
        }
        else
        {
            _logger.LogWarning("Sync point for query {QueryId} already exists", queryId);
        }
        
        return Task.CompletedTask;
    }
}