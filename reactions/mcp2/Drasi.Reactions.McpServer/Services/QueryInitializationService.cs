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
using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.McpServer.Models;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.McpServer.Services;

public interface IQueryInitializationService
{
    Task InitializeAllQueries();
}

public class QueryInitializationService : IQueryInitializationService
{
    private readonly IManagementClient _managementClient;
    private readonly IResultViewClient _viewClient;
    private readonly IQueryConfigService _queryConfigService;
    private readonly IResourceStoreService _resourceStore;
    private readonly ILogger<QueryInitializationService> _logger;

    public QueryInitializationService(
        IManagementClient managementClient,
        IResultViewClient viewClient,
        IQueryConfigService queryConfigService,
        IResourceStoreService resourceStore,
        ILogger<QueryInitializationService> logger)
    {
        _managementClient = managementClient;
        _viewClient = viewClient;
        _queryConfigService = queryConfigService;
        _resourceStore = resourceStore;
        _logger = logger;
    }

    public async Task InitializeAllQueries()
    {
        var queryNames = _queryConfigService.GetQueryNames();
        _logger.LogInformation("Initializing {Count} queries", queryNames.Count());

        foreach (var queryName in queryNames)
        {
            try
            {
                await InitializeQuery(queryName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize query {QueryName}", queryName);
                throw;
            }
        }
    }

    private async Task InitializeQuery(string queryName)
    {
        _logger.LogInformation("Initializing query {QueryName}", queryName);

        // Get query configuration
        var config = _queryConfigService.GetQueryConfig<QueryConfig>(queryName);
        if (config == null)
        {
            throw new InvalidOperationException($"No configuration found for query {queryName}");
        }

        // Get query container ID
        var containerId = await _managementClient.GetQueryContainerId(queryName);
        if (string.IsNullOrEmpty(containerId))
        {
            _logger.LogWarning("No container ID found for query {QueryName}, skipping initialization", queryName);
            return;
        }

        // Fetch current results
        _logger.LogDebug("Fetching current results for query {QueryName} from container {ContainerId}", queryName, containerId);
        
        var count = 0;
        var stream = _viewClient.GetCurrentResult(queryName, default);
        await foreach (var item in stream)
        {
            try
            {
                // Skip header item
                if (item.Header != null)
                {
                    _logger.LogDebug("Skipping header item for query {QueryName}", queryName);
                    continue;
                }

                if (item.Data != null)
                {
                    var jsonData = JsonSerializer.SerializeToElement(item.Data);
                    var key = ExtractKey(jsonData, config.KeyField);
                    if (key != null)
                    {
                        _resourceStore.UpdateEntry(queryName, key, jsonData, config);
                        count++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process result item for query {QueryName}", queryName);
            }
        }

        _logger.LogInformation("Initialized query {QueryName} with {Count} entries", queryName, count);
    }

    private string? ExtractKey(JsonElement data, string keyField)
    {
        if (data.TryGetProperty(keyField, out var keyElement))
        {
            return keyElement.ToString();
        }
        
        _logger.LogWarning("Key field '{KeyField}' not found in data", keyField);
        return null;
    }
}