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

using Drasi.Reaction.SDK.Models.ViewService;
using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.Mcp.Interfaces;
using Drasi.Reactions.Mcp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Drasi.Reactions.Mcp.Services;

/// <summary>
/// Service responsible for initializing queries and syncing initial data
/// </summary>
public class QueryInitializationService
{
    private readonly ILogger<QueryInitializationService> _logger;
    private readonly IQueryConfigService _queryConfigService;
    private readonly IMcpResourceStore _resourceStore;
    private readonly IResultViewClient _resultViewClient;
    private readonly IExtendedManagementClient _managementClient;
    private readonly IMcpSyncPointManager _syncPointManager;
    private readonly IErrorStateHandler _errorStateHandler;
    private readonly string _reactionName;

    public const int DefaultWaitForQueryReadySeconds = 300; // 5 minutes

    public QueryInitializationService(
        ILogger<QueryInitializationService> logger,
        IQueryConfigService queryConfigService,
        IMcpResourceStore resourceStore,
        IResultViewClient resultViewClient,
        IExtendedManagementClient managementClient,
        IMcpSyncPointManager syncPointManager,
        IErrorStateHandler errorStateHandler,
        IConfiguration configuration)
    {
        _logger = logger;
        _queryConfigService = queryConfigService;
        _resourceStore = resourceStore;
        _resultViewClient = resultViewClient;
        _managementClient = managementClient;
        _syncPointManager = syncPointManager;
        _errorStateHandler = errorStateHandler;
        _reactionName = configuration["REACTION_NAME"] ?? "drasi-mcp-reaction";
    }

    public async Task InitializeAllQueriesAsync()
    {
        var queryNames = _queryConfigService.GetQueryNames();
        if (!queryNames.Any())
        {
            _logger.LogWarning("No queries configured for initialization");
            return;
        }

        foreach (var queryName in queryNames)
        {
            try
            {
                await InitializeQueryAsync(queryName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize query {QueryName}", queryName);
                await _errorStateHandler.HandleFatalErrorAsync(ex, $"Failed to initialize query {queryName}");
                throw;
            }
        }
    }

    private async Task InitializeQueryAsync(string queryName)
    {
        _logger.LogInformation("Initializing query {QueryName}", queryName);

        var config = _queryConfigService.GetQueryConfig<McpQueryConfig>(queryName);
        if (config == null)
        {
            var errorMessage = $"Query config is null for query {queryName}";
            _logger.LogError(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        // Step 1: Check if sync point already exists (resuming vs fresh start)
        var existingSyncPoint = _syncPointManager.GetSyncPoint(queryName);
        if (existingSyncPoint.HasValue)
        {
            _logger.LogInformation("Query {QueryName} already initialized with sync point {SyncPoint}, skipping initialization", 
                queryName, existingSyncPoint.Value);
            return;
        }

        // Step 2: Wait for query to be ready
        _logger.LogInformation("Waiting for query {QueryName} to be ready...", queryName);
        if (!await _managementClient.WaitForQueryReadyAsync(queryName, DefaultWaitForQueryReadySeconds, CancellationToken.None))
        {
            var errorMessage = $"Query {queryName} did not become ready within {DefaultWaitForQueryReadySeconds} seconds";
            _logger.LogError(errorMessage);
            await _errorStateHandler.HandleFatalErrorAsync(new TimeoutException(errorMessage), errorMessage);
            throw new TimeoutException(errorMessage);
        }

        // Step 3: Initialize query metadata in store
        await _resourceStore.InitializeQueryAsync(queryName, config.KeyField, config.Description);

        // Step 4: Fetch initial results and sync point
        try
        {
            _logger.LogDebug("Fetching initial results for query {QueryName}", queryName);
            var syncPoint = await PerformInitialSyncAsync(queryName, config);
            
            // Step 5: Initialize sync point
            await _syncPointManager.InitializeSyncPointAsync(queryName, syncPoint);
            
            _logger.LogInformation("Successfully initialized query {QueryName} with sync point {SyncPoint}", 
                queryName, syncPoint);
        }
        catch (Exception ex)
        {
            var errorMessage = $"Failed to perform initial sync for query {queryName}";
            _logger.LogError(ex, errorMessage);
            await _errorStateHandler.HandleFatalErrorAsync(ex, errorMessage);
            throw;
        }
    }

    private async Task<long> PerformInitialSyncAsync(string queryName, McpQueryConfig config)
    {
        long syncPoint = -1;
        var count = 0;
        var itemsToProcess = new List<(string key, object data)>();

        var stream = _resultViewClient.GetCurrentResult(queryName);
        await using var enumerator = stream.GetAsyncEnumerator();

        // Get sync point from header
        if (await enumerator.MoveNextAsync())
        {
            var firstItem = enumerator.Current;
            if (firstItem?.Header != null)
            {
                syncPoint = firstItem.Header.Sequence;
                _logger.LogDebug("Got sync point {SyncPoint} from header for query {QueryName}", 
                    syncPoint, queryName);
            }
            else
            {
                _logger.LogWarning("No header found in result stream for query {QueryName}, using default sync point", 
                    queryName);
            }

            // Process first item if it has data
            if (firstItem?.Data != null)
            {
                var key = ExtractKeyValue(firstItem.Data, config.KeyField);
                if (key != null)
                {
                    itemsToProcess.Add((key, firstItem.Data));
                }
            }
        }

        // Process remaining items
        while (await enumerator.MoveNextAsync())
        {
            var viewItem = enumerator.Current;
            if (viewItem?.Data == null)
                continue;
                
            var key = ExtractKeyValue(viewItem.Data, config.KeyField);
            if (key == null)
            {
                _logger.LogWarning("Could not extract key field {KeyField} from initial result", 
                    config.KeyField);
                continue;
            }

            itemsToProcess.Add((key, viewItem.Data));
        }

        // Bulk process all items
        foreach (var (key, data) in itemsToProcess)
        {
            var resource = new McpResource
            {
                Uri = $"drasi://{_reactionName}/entries/{queryName}/{key}",
                QueryId = queryName,
                EntryKey = key,
                Data = data,
                ContentType = config.ResourceContentType,
                LastUpdated = DateTime.UtcNow
            };

            await _resourceStore.UpsertResourceAsync(resource);
            count++;
        }

        _logger.LogInformation("Loaded {Count} initial results for query {QueryName}", count, queryName);
        return syncPoint;
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