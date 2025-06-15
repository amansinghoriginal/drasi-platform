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
using Drasi.Reactions.Mcp.Interfaces;
using Drasi.Reactions.Mcp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Drasi.Reactions.Mcp.Services;

public class McpChangeHandler : IChangeEventHandler<McpQueryConfig>
{
    private readonly ILogger<McpChangeHandler> _logger;
    private readonly IMcpResourceStore _resourceStore;
    private readonly IMcpSyncPointManager _syncPointManager;
    private readonly IMcpNotifier? _notifier;
    private readonly string _reactionName;

    public McpChangeHandler(
        ILogger<McpChangeHandler> logger,
        IMcpResourceStore resourceStore,
        IMcpSyncPointManager syncPointManager,
        IConfiguration configuration,
        IMcpNotifier? notifier = null)
    {
        _logger = logger;
        _resourceStore = resourceStore;
        _syncPointManager = syncPointManager;
        _notifier = notifier;
        _reactionName = configuration["REACTION_NAME"] ?? "drasi-mcp-reaction";
    }

    public async Task HandleChange(ChangeEvent evt, McpQueryConfig? queryConfig)
    {
        if (queryConfig == null)
        {
            throw new ArgumentNullException(nameof(queryConfig), 
                $"Query config is null for query {evt.QueryId}. Cannot process change event.");
        }

        _logger.LogInformation("Processing change event from query {QueryId} with sequence {Sequence}", 
            evt.QueryId, evt.Sequence);

        // Check if query was initialized
        var syncPoint = _syncPointManager.GetSyncPoint(evt.QueryId);
        if (syncPoint == null)
        {
            var message = $"Received change event for query {evt.QueryId} which was not yet initialized";
            _logger.LogWarning(message);
            throw new InvalidOperationException(message); // Forces re-delivery
        }

        // Skip events older than current sync point
        if (evt.Sequence <= syncPoint.Value)
        {
            _logger.LogInformation("Skipping change event {Sequence} for query {QueryId} as it is older than current sync point {SyncPoint}",
                evt.Sequence, evt.QueryId, syncPoint.Value);
            return;
        }

        try
        {
            var processedCount = 0;

            // Process added results
            if (evt.AddedResults != null)
            {
                foreach (var addedResult in evt.AddedResults)
                {
                    var key = ExtractKeyValue(addedResult, queryConfig.KeyField);
                    if (key == null)
                    {
                        _logger.LogWarning("Could not extract key field {KeyField} from added result", queryConfig.KeyField);
                        continue;
                    }

                    var resource = new McpResource
                    {
                        Uri = $"drasi://{_reactionName}/entries/{evt.QueryId}/{key}",
                        QueryId = evt.QueryId,
                        EntryKey = key,
                        Data = addedResult,
                        ContentType = queryConfig.ResourceContentType,
                        LastUpdated = DateTime.UtcNow
                    };

                    await _resourceStore.UpsertResourceAsync(resource);
                    _logger.LogInformation("Added resource with key {Key} for query {QueryId}", key, evt.QueryId);
                    
                    // Send MCP notification
                    if (_notifier != null)
                    {
                        await _notifier.NotifyResourceCreatedAsync(resource.Uri);
                    }
                    processedCount++;
                }
            }

            // Process updated results
            if (evt.UpdatedResults != null)
            {
                foreach (var updatedResult in evt.UpdatedResults)
                {
                    var key = ExtractKeyValue(updatedResult.After, queryConfig.KeyField);
                    if (key == null)
                    {
                        _logger.LogWarning("Could not extract key field {KeyField} from updated result", queryConfig.KeyField);
                        continue;
                    }

                    var resource = new McpResource
                    {
                        Uri = $"drasi://{_reactionName}/entries/{evt.QueryId}/{key}",
                        QueryId = evt.QueryId,
                        EntryKey = key,
                        Data = updatedResult.After,
                        ContentType = queryConfig.ResourceContentType,
                        LastUpdated = DateTime.UtcNow
                    };

                    await _resourceStore.UpsertResourceAsync(resource);
                    _logger.LogInformation("Updated resource with key {Key} for query {QueryId}", key, evt.QueryId);
                    
                    // Send MCP notification
                    if (_notifier != null)
                    {
                        await _notifier.NotifyResourceUpdatedAsync(resource.Uri);
                    }
                    processedCount++;
                }
            }

            // Process deleted results
            if (evt.DeletedResults != null)
            {
                foreach (var deletedResult in evt.DeletedResults)
                {
                    var key = ExtractKeyValue(deletedResult, queryConfig.KeyField);
                    if (key == null)
                    {
                        _logger.LogWarning("Could not extract key field {KeyField} from deleted result", queryConfig.KeyField);
                        continue;
                    }

                    await _resourceStore.DeleteEntryAsync(evt.QueryId, key);
                    _logger.LogInformation("Deleted resource with key {Key} for query {QueryId}", key, evt.QueryId);
                    
                    // Send MCP notification
                    if (_notifier != null)
                    {
                        var uri = $"drasi://{_reactionName}/entries/{evt.QueryId}/{key}";
                        await _notifier.NotifyResourceDeletedAsync(uri);
                    }
                    processedCount++;
                }
            }

            // Update sync point after successful processing
            await _syncPointManager.UpdateSyncPointAsync(evt.QueryId, evt.Sequence);
            
            _logger.LogInformation("Successfully processed change event sequence {Sequence} for query {QueryId} ({ProcessedCount} items)",
                evt.Sequence, evt.QueryId, processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process change event sequence {Sequence} for query {QueryId}",
                evt.Sequence, evt.QueryId);
            throw; // Re-throw to trigger event re-delivery
        }
    }

    private string? ExtractKeyValue(object item, string keyField)
    {
        try
        {
            if (item is Dictionary<string, object> dict)
            {
                if (dict.TryGetValue(keyField, out var value))
                {
                    return value?.ToString();
                }
            }
            else if (item is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty(keyField, out JsonElement keyValue))
                {
                    return keyValue.ToString();
                }
            }
            else
            {
                // Try to serialize and deserialize to handle other object types
                var json = JsonSerializer.Serialize(item);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(keyField, out JsonElement keyValue))
                {
                    return keyValue.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting key field {KeyField} from item", keyField);
        }

        return null;
    }
}