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
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reactions.McpQueryResultsServer.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Handlers;

public class ChangeEventHandler : IChangeEventHandler<QueryConfig>
{
    private readonly IQueryResultStore _resultStore;
    private readonly IMcpNotificationService _notificationService;
    private readonly ILogger<ChangeEventHandler> _logger;
    
    public ChangeEventHandler(
        IQueryResultStore resultStore,
        IMcpNotificationService notificationService,
        ILogger<ChangeEventHandler> logger)
    {
        _resultStore = resultStore;
        _notificationService = notificationService;
        _logger = logger;
    }
    
    public async Task HandleChange(ChangeEvent changeEvent, QueryConfig? config)
    {
        if (config == null)
        {
            _logger.LogWarning("No configuration for query {QueryId}", changeEvent.QueryId);
            return;
        }
        
        try
        {
            // Apply changes to store
            await _resultStore.ApplyChangeEventAsync(changeEvent, config);
            
            
            // Determine notifications to send
            var notificationsToSend = new List<string>();
            
            // Query-level notification for additions/deletions
            if (changeEvent.AddedResults.Any() || changeEvent.DeletedResults.Any())
            {
                var queryUri = $"drasi://queries/{changeEvent.QueryId}";
                notificationsToSend.Add(queryUri);
                
                _logger.LogDebug(
                    "Query {QueryId} membership changed: +{Added} -{Deleted}", 
                    changeEvent.QueryId,
                    changeEvent.AddedResults.Count(),
                    changeEvent.DeletedResults.Count());
            }
            
            // Entry-level notifications for updates
            foreach (var updated in changeEvent.UpdatedResults)
            {
                if (updated.After.TryGetValue(config.KeyField, out var keyObj))
                {
                    var key = keyObj?.ToString();
                    if (!string.IsNullOrEmpty(key))
                    {
                        var entryUri = $"drasi://entries/{changeEvent.QueryId}/{key}";
                        notificationsToSend.Add(entryUri);
                    }
                }
            }
            
            // Send notifications following MCP protocol
            foreach (var uri in notificationsToSend)
            {
                await _notificationService.SendResourceUpdatedNotificationAsync(uri, CancellationToken.None);
            }
            
            _logger.LogInformation(
                "Processed change event for query {QueryId}: +{Added} ~{Updated} -{Deleted}, sent {NotificationCount} notifications",
                changeEvent.QueryId,
                changeEvent.AddedResults.Count(),
                changeEvent.UpdatedResults.Count(),
                changeEvent.DeletedResults.Count(),
                notificationsToSend.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing change event for query {QueryId}", changeEvent.QueryId);
            throw;
        }
    }
}