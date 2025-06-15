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
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;

namespace Drasi.Reactions.Mcp.Services;

// MCP resource provider - will be registered with MCP server
public class McpResourceProvider
{
    private readonly IMcpResourceStore _resourceStore;
    private readonly ILogger<McpResourceProvider> _logger;

    public McpResourceProvider(
        IMcpResourceStore resourceStore,
        ILogger<McpResourceProvider> logger)
    {
        _resourceStore = resourceStore;
        _logger = logger;
    }

    [Description("List all available Drasi query resources")]
    public async Task<IEnumerable<DrasiResource>> ListResources()
    {
        var queries = await _resourceStore.GetAvailableQueriesAsync();
        var resources = new List<DrasiResource>();

        // Only return query-level resources for discoverability
        // Individual entries are discovered by reading the query resource
        foreach (var query in queries)
        {
            resources.Add(new DrasiResource
            {
                Uri = query.Uri,
                Name = $"Query: {query.QueryId}",
                Description = query.Data?.ToString() ?? $"Live results from Drasi query: {query.QueryId}",
                MimeType = query.ContentType
            });
        }

        _logger.LogInformation("Listed {Count} query resources", resources.Count);
        return resources;
    }

    [Description("Read a specific Drasi resource by URI")]
    public async Task<DrasiResourceContent?> ReadResource(string uri)
    {
        var parts = ParseDrasiUri(uri);
        if (parts == null) return null;

        if (parts.Value.IsQueryLevel)
        {
            // Return list of entry URIs for this query
            var entries = await _resourceStore.GetQueryEntriesAsync(parts.Value.QueryId);
            var entryUris = entries.Select(e => e.Uri).ToList();

            return new DrasiResourceContent
            {
                Uri = uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(new
                {
                    queryId = parts.Value.QueryId,
                    entryCount = entryUris.Count,
                    entries = entryUris
                })
            };
        }
        else
        {
            // Return specific entry data
            var resource = await _resourceStore.GetResourceByUriAsync(uri);
            if (resource == null) return null;

            return new DrasiResourceContent
            {
                Uri = uri,
                MimeType = resource.ContentType,
                Text = JsonSerializer.Serialize(resource.Data)
            };
        }
    }

    private (string QueryId, string? EntryKey, bool IsQueryLevel)? ParseDrasiUri(string uri)
    {
        try
        {
            // Expected formats:
            // drasi://<reaction-name>/queries/<query-id>
            // drasi://<reaction-name>/entries/<query-id>/<entry-key>

            if (!uri.StartsWith("drasi://"))
                return null;

            var parts = uri.Substring(8).Split('/'); // Remove "drasi://"
            if (parts.Length < 3)
                return null;

            var resourceType = parts[1];
            var queryId = parts[2];

            if (resourceType == "queries")
            {
                return (queryId, null, true);
            }
            else if (resourceType == "entries" && parts.Length >= 4)
            {
                var entryKey = string.Join("/", parts.Skip(3)); // Handle keys with slashes
                return (queryId, entryKey, false);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}