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

using System.Text.Json;
using Drasi.Reaction.SDK;
using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.McpServer.Services;

public class ChangeEventHandler : IChangeEventHandler<QueryConfig>
{
    private readonly IResourceStoreService _resourceStore;
    private readonly ILogger<ChangeEventHandler> _logger;

    public ChangeEventHandler(
        IResourceStoreService resourceStore,
        ILogger<ChangeEventHandler> logger)
    {
        _resourceStore = resourceStore;
        _logger = logger;
    }

    public async Task HandleChange(ChangeEvent evt, QueryConfig? queryConfig)
    {
        if (queryConfig == null)
        {
            _logger.LogWarning("No configuration provided for query {QueryId}", evt.QueryId);
            return;
        }

        _logger.LogInformation(
            "Processing change event from query {QueryId}: {AddedCount} added, {UpdatedCount} updated, {DeletedCount} deleted",
            evt.QueryId, evt.AddedResults?.Length ?? 0, evt.UpdatedResults?.Length ?? 0, evt.DeletedResults?.Length ?? 0);

        // Process added results
        if (evt.AddedResults != null)
        {
            foreach (var added in evt.AddedResults)
            {
                var key = ExtractKey(added, queryConfig.KeyField);
                if (key != null)
                {
                    var jsonElement = ConvertToJsonElement(added);
                    _resourceStore.UpdateEntry(evt.QueryId, key, jsonElement, queryConfig);
                }
            }
        }

        // Process updated results
        if (evt.UpdatedResults != null)
        {
            foreach (var updated in evt.UpdatedResults)
            {
                var key = ExtractKey(updated.After, queryConfig.KeyField);
                if (key != null)
                {
                    var jsonElement = ConvertToJsonElement(updated.After);
                    _resourceStore.UpdateEntry(evt.QueryId, key, jsonElement, queryConfig);
                }
            }
        }

        // Process deleted results
        if (evt.DeletedResults != null)
        {
            foreach (var deleted in evt.DeletedResults)
            {
                var key = ExtractKey(deleted, queryConfig.KeyField);
                if (key != null)
                {
                    _resourceStore.RemoveEntry(evt.QueryId, key);
                }
            }
        }

        await Task.CompletedTask;
    }

    private string? ExtractKey(Dictionary<string, object> data, string keyField)
    {
        if (data != null && data.TryGetValue(keyField, out var keyValue))
        {
            return keyValue?.ToString();
        }
        
        _logger.LogWarning("Key field '{KeyField}' not found in data", keyField);
        return null;
    }

    private JsonElement ConvertToJsonElement(Dictionary<string, object> data)
    {
        var json = JsonSerializer.Serialize(data);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}