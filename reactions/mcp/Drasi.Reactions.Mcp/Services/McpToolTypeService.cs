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
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Drasi.Reactions.Mcp.Services;

/// <summary>
/// MCP tool type service that provides tools for listing and reading Drasi resources
/// </summary>
[McpServerToolType]
public class McpToolTypeService
{
    private static IMcpResourceStore? _resourceStore;
    private static ILogger<McpToolTypeService>? _logger;

    public static void Initialize(IMcpResourceStore resourceStore, ILogger<McpToolTypeService> logger)
    {
        _resourceStore = resourceStore;
        _logger = logger;
    }

    [McpServerTool(Name = "list_drasi_resources")]
    [Description("List all available Drasi query resources")]
    public static async Task<object> ListResources()
    {
        if (_resourceStore == null || _logger == null)
            throw new InvalidOperationException("McpToolTypeService not initialized");

        var queries = await _resourceStore.GetAvailableQueriesAsync();
        var resources = new List<object>();

        foreach (var query in queries)
        {
            resources.Add(new
            {
                uri = query.Uri,
                name = $"Query: {query.QueryId}",
                description = query.Data?.ToString() ?? $"Live results from Drasi query: {query.QueryId}",
                mimeType = query.ContentType
            });
        }

        _logger.LogInformation("Listed {Count} query resources", resources.Count);
        
        return new { resources = resources };
    }

    [McpServerTool(Name = "read_drasi_resource")]
    [Description("Read a specific Drasi resource by URI")]
    public static async Task<object> ReadResource(string uri)
    {
        if (_resourceStore == null || _logger == null)
            throw new InvalidOperationException("McpToolTypeService not initialized");

        var parts = ParseDrasiUri(uri);
        if (parts == null)
        {
            return new
            {
                error = "Invalid Drasi URI format",
                uri = uri
            };
        }

        if (parts.Value.IsQueryLevel)
        {
            // Return list of entry URIs for this query
            var entries = await _resourceStore.GetQueryEntriesAsync(parts.Value.QueryId);
            var entryUris = entries.Select(e => e.Uri).ToList();

            return new
            {
                uri = uri,
                mimeType = "application/json",
                content = new
                {
                    queryId = parts.Value.QueryId,
                    entryCount = entryUris.Count,
                    entries = entryUris
                }
            };
        }
        else
        {
            // Return specific entry data
            var resource = await _resourceStore.GetResourceByUriAsync(uri);
            if (resource == null)
            {
                return new
                {
                    error = "Resource not found",
                    uri = uri
                };
            }

            return new
            {
                uri = uri,
                mimeType = resource.ContentType,
                content = resource.Data
            };
        }
    }

    private static (string QueryId, string? EntryKey, bool IsQueryLevel)? ParseDrasiUri(string uri)
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