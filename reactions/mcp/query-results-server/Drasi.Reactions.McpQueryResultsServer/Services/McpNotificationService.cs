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

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelContextProtocol;

namespace Drasi.Reactions.McpQueryResultsServer.Services;

public interface IMcpNotificationService
{
    Task SendResourceUpdatedNotificationAsync(string resourceUri, CancellationToken cancellationToken = default);
}

public class McpNotificationService : IMcpNotificationService
{
    private readonly ILogger<McpNotificationService> _logger;
    private readonly ISessionTracker _sessionTracker;

    public McpNotificationService(
        ILogger<McpNotificationService> logger,
        ISessionTracker sessionTracker)
    {
        _logger = logger;
        _sessionTracker = sessionTracker;
    }

    public async Task SendResourceUpdatedNotificationAsync(string resourceUri, CancellationToken cancellationToken = default)
    {
        var activeSessions = _sessionTracker.GetActiveSessions().ToList();
        if (activeSessions.Count == 0)
        {
            _logger.LogDebug("No active MCP sessions to notify for resource update: {Uri}", resourceUri);
            return;
        }

        var notification = new ResourceUpdatedNotificationParams { Uri = resourceUri };
        var notificationTasks = new List<Task>();

        foreach (var server in activeSessions)
        {
            notificationTasks.Add(SendNotificationToSessionAsync(server, notification, resourceUri, cancellationToken));
        }

        await Task.WhenAll(notificationTasks);
        _logger.LogDebug("Sent resource update notification for {Uri} to {Count} sessions", resourceUri, notificationTasks.Count);
    }

    private async Task SendNotificationToSessionAsync(
        IMcpServer server, 
        ResourceUpdatedNotificationParams notification, 
        string resourceUri,
        CancellationToken cancellationToken)
    {
        try
        {
            await server.SendNotificationAsync(
                NotificationMethods.ResourceUpdatedNotification,
                notification,
                serializerOptions: null,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification to session for resource {Uri}", resourceUri);
        }
    }
}