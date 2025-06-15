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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.Mcp.Services;

/// <summary>
/// Implementation of MCP notifier that queues notifications to be sent via MCP protocol
/// </summary>
public class McpNotifier : IMcpNotifier
{
    private readonly ILogger<McpNotifier> _logger;
    private readonly IServiceProvider _serviceProvider;

    public McpNotifier(ILogger<McpNotifier> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task NotifyResourceCreatedAsync(string uri)
    {
        _logger.LogInformation("Resource created: {Uri}", uri);
        // TODO: Send MCP notifications when notification service is ready
        await Task.CompletedTask;
    }

    public async Task NotifyResourceUpdatedAsync(string uri)
    {
        _logger.LogInformation("Resource updated: {Uri}", uri);
        // TODO: Send MCP notifications when notification service is ready
        await Task.CompletedTask;
    }

    public async Task NotifyResourceDeletedAsync(string uri)
    {
        _logger.LogInformation("Resource deleted: {Uri}", uri);
        // TODO: Send MCP notifications when notification service is ready
        await Task.CompletedTask;
    }

    public async Task NotifyResourcesChangedAsync(IEnumerable<string> uris)
    {
        foreach (var uri in uris)
        {
            _logger.LogInformation("Resource changed: {Uri}", uri);
        }
        // TODO: Send MCP notifications when notification service is ready
        await Task.CompletedTask;
    }
    
    public async Task NotifyResourceSubscribedAsync(string uri)
    {
        _logger.LogInformation("Resource subscribed: {Uri}", uri);
        // No specific MCP notification for subscriptions, just log
        await Task.CompletedTask;
    }
    
    public async Task NotifyResourceUnsubscribedAsync(string uri)
    {
        _logger.LogInformation("Resource unsubscribed: {Uri}", uri);
        // No specific MCP notification for unsubscriptions, just log
        await Task.CompletedTask;
    }
}