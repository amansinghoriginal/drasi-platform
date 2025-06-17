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
using System.Text.Json;
using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.McpQueryResultsServer.Models;

namespace Drasi.Reactions.McpQueryResultsServer.Services;

public class QueryResultStore : IQueryResultStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, JsonElement>> _queryResults = new();
    private readonly ILogger<QueryResultStore> _logger;
    
    public QueryResultStore(ILogger<QueryResultStore> logger)
    {
        _logger = logger;
    }
    
    public Task<IEnumerable<string>> GetQueryEntriesAsync(string queryId)
    {
        if (_queryResults.TryGetValue(queryId, out var entries))
        {
            return Task.FromResult(entries.Keys.AsEnumerable());
        }
        
        return Task.FromResult(Enumerable.Empty<string>());
    }
    
    public Task<JsonElement?> GetEntryAsync(string queryId, string entryKey)
    {
        if (_queryResults.TryGetValue(queryId, out var entries))
        {
            if (entries.TryGetValue(entryKey, out var entry))
            {
                return Task.FromResult<JsonElement?>(entry);
            }
        }
        
        return Task.FromResult<JsonElement?>(null);
    }
    
    public Task ApplyChangeEventAsync(ChangeEvent changeEvent, QueryConfig config)
    {
        var entries = _queryResults.GetOrAdd(changeEvent.QueryId, _ => new ConcurrentDictionary<string, JsonElement>());
        
        // Process additions
        foreach (var added in changeEvent.AddedResults)
        {
            if (added.TryGetValue(config.KeyField, out var keyObj))
            {
                var key = keyObj?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    // Convert Dictionary to JsonElement
                    var json = JsonSerializer.Serialize(added);
                    entries[key] = JsonSerializer.Deserialize<JsonElement>(json);
                    _logger.LogDebug("Added entry {Key} to query {QueryId}", key, changeEvent.QueryId);
                }
            }
        }
        
        // Process updates
        foreach (var updated in changeEvent.UpdatedResults)
        {
            if (updated.After.TryGetValue(config.KeyField, out var keyObj))
            {
                var key = keyObj?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    // Convert Dictionary to JsonElement
                    var json = JsonSerializer.Serialize(updated.After);
                    entries[key] = JsonSerializer.Deserialize<JsonElement>(json);
                    _logger.LogDebug("Updated entry {Key} in query {QueryId}", key, changeEvent.QueryId);
                }
            }
        }
        
        // Process deletions
        foreach (var deleted in changeEvent.DeletedResults)
        {
            if (deleted.TryGetValue(config.KeyField, out var keyObj))
            {
                var key = keyObj?.ToString();
                if (!string.IsNullOrEmpty(key))
                {
                    entries.TryRemove(key, out _);
                    _logger.LogDebug("Deleted entry {Key} from query {QueryId}", key, changeEvent.QueryId);
                }
            }
        }
        
        _logger.LogInformation(
            "Applied changes to query {QueryId}: +{Added} ~{Updated} -{Deleted}, total entries: {Total}",
            changeEvent.QueryId,
            changeEvent.AddedResults.Count(),
            changeEvent.UpdatedResults.Count(),
            changeEvent.DeletedResults.Count(),
            entries.Count);
        
        return Task.CompletedTask;
    }
}