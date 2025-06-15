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
using Drasi.Reactions.Mcp.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Drasi.Reactions.Mcp.Services;

public class InMemoryMcpResourceStore : IMcpResourceStore
{
    private readonly ILogger<InMemoryMcpResourceStore> _logger;
    private readonly ConcurrentDictionary<string, McpResource> _resources = new();
    private readonly ConcurrentDictionary<string, QueryMetadata> _queries = new();
    private readonly string _reactionName;

    private class QueryMetadata
    {
        public string QueryId { get; set; } = string.Empty;
        public string KeyField { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public InMemoryMcpResourceStore(
        ILogger<InMemoryMcpResourceStore> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _reactionName = configuration["REACTION_NAME"] ?? "drasi-mcp-reaction";
    }

    public Task<IEnumerable<McpResource>> GetAvailableQueriesAsync()
    {
        var queryResources = _queries.Values.Select(q => new McpResource
        {
            Uri = $"drasi://{_reactionName}/queries/{q.QueryId}",
            QueryId = q.QueryId,
            Data = new { description = q.Description ?? $"Drasi query: {q.QueryId}" },
            ContentType = "application/json"
        });

        return Task.FromResult(queryResources);
    }

    public Task<IEnumerable<McpResource>> GetQueryEntriesAsync(string queryId)
    {
        var entries = _resources.Values
            .Where(r => r.QueryId == queryId && r.EntryKey != null)
            .OrderBy(r => r.EntryKey)
            .AsEnumerable();

        return Task.FromResult(entries);
    }

    public Task<McpResource?> GetEntryAsync(string queryId, string entryKey)
    {
        var uri = $"drasi://{_reactionName}/entries/{queryId}/{entryKey}";
        _resources.TryGetValue(uri, out var resource);
        return Task.FromResult(resource);
    }

    public Task<McpResource?> GetResourceByUriAsync(string uri)
    {
        _resources.TryGetValue(uri, out var resource);
        return Task.FromResult(resource);
    }

    public Task UpsertResourceAsync(McpResource resource)
    {
        _resources.AddOrUpdate(resource.Uri, resource, (key, existing) => resource);
        _logger.LogDebug("Upserted resource {Uri}", resource.Uri);
        return Task.CompletedTask;
    }

    public Task DeleteResourceAsync(string uri)
    {
        if (_resources.TryRemove(uri, out _))
        {
            _logger.LogDebug("Deleted resource {Uri}", uri);
        }
        return Task.CompletedTask;
    }

    public Task DeleteEntryAsync(string queryId, string entryKey)
    {
        var uri = $"drasi://{_reactionName}/entries/{queryId}/{entryKey}";
        return DeleteResourceAsync(uri);
    }

    public Task<bool> InitializeQueryAsync(string queryId, string keyField, string? description = null)
    {
        var metadata = new QueryMetadata
        {
            QueryId = queryId,
            KeyField = keyField,
            Description = description
        };

        _queries.AddOrUpdate(queryId, metadata, (key, existing) => metadata);
        _logger.LogInformation("Initialized query {QueryId} with key field {KeyField}", queryId, keyField);
        return Task.FromResult(true);
    }
}