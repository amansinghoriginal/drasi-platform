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

using System.ComponentModel;
using System.Text.Json;
using Drasi.Reactions.McpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Drasi.Reactions.McpServer.Resources;

/// <summary>
/// MCP resource type for exposing Drasi query results.
/// Implements the two-level resource hierarchy: queries and entries.
/// </summary>
[McpServerResourceType]
public class DrasiResourceType
{
    private readonly IResourceStoreService _resourceStore;
    private readonly ILogger<DrasiResourceType> _logger;
    private readonly string _reactionName;

    public DrasiResourceType(
        IResourceStoreService resourceStore,
        ILogger<DrasiResourceType> logger,
        IConfiguration configuration)
    {
        _resourceStore = resourceStore;
        _logger = logger;
        _reactionName = configuration["REACTION_NAME"] ?? "mcp-server";
    }

    /// <summary>
    /// Lists all Drasi query resources.
    /// </summary>
    [McpServerResource(UriTemplate = "queries", Name = "Drasi Queries", MimeType = "application/json")]
    [Description("List of all available Drasi queries")]
    public ResourceContents ListQueries()
    {
        _logger.LogDebug("Listing all Drasi queries");
        
        var queryResources = _resourceStore.ListQueryResources(); // Use new method from store

        var content = new
        {
            queries = queryResources.Select(r => new
            {
                uri = r.Uri,
                name = r.Name,
                description = r.Description
            }).ToArray()
        };

        return new TextResourceContents
        {
            Text = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }),
            MimeType = "application/json",
            Uri = $"drasi://{_reactionName}/queries"
        };
    }

    /// <summary>
    /// Gets a specific Drasi query resource.
    /// </summary>
    [McpServerResource(UriTemplate = "queries/{queryId}", Name = "Drasi Query")]
    [Description("A specific Drasi query with its current entries")]
    public ResourceContents GetQuery(RequestContext<ReadResourceRequestParams> requestContext, string queryId)
    {
        var uri = requestContext.Params?.Uri ?? $"drasi://{_reactionName}/queries/{queryId}";
        _logger.LogDebug("Reading query resource: {Uri}", uri);
        
        var resource = _resourceStore.GetResource(uri);
        if (resource == null)
        {
            throw new NotSupportedException($"Query not found: {queryId}");
        }

        // For query-level resources, return the list of entry URIs
        if (resource.Content != null)
        {
            var text = resource.Content is JsonElement jsonElement 
                ? jsonElement.GetRawText() 
                : JsonSerializer.Serialize(resource.Content);

            return new TextResourceContents
            {
                Text = text,
                MimeType = resource.MimeType ?? "application/json",
                Uri = uri
            };
        }

        return new TextResourceContents
        {
            Text = "{}",
            MimeType = "application/json",
            Uri = uri
        };
    }

    /// <summary>
    /// Gets entries for a specific Drasi query.
    /// </summary>
    [McpServerResource(UriTemplate = "queries/{queryId}/entries", Name = "Query Entries")]
    [Description("All entries for a specific Drasi query")]
    public ResourceContents GetQueryEntries(RequestContext<ReadResourceRequestParams> requestContext, string queryId)
    {
        var uri = $"drasi://{_reactionName}/queries/{queryId}/entries";
        _logger.LogDebug("Reading query entries: {Uri}", uri);
        
        var entries = _resourceStore.GetQueryEntries(queryId);

        var content = new
        {
            queryId,
            entries = entries.Select(e => new
            {
                uri = e.Uri,
                name = e.Name,
                mimeType = e.MimeType
            }).ToArray()
        };

        return new TextResourceContents
        {
            Text = JsonSerializer.Serialize(content, new JsonSerializerOptions { WriteIndented = true }),
            MimeType = "application/json",
            Uri = uri
        };
    }

    /// <summary>
    /// Gets a specific entry from a Drasi query.
    /// </summary>
    [McpServerResource(UriTemplate = "queries/{queryId}/entries/{entryId}", Name = "Query Entry")]
    [Description("A specific entry from a Drasi query")]
    public ResourceContents GetQueryEntry(RequestContext<ReadResourceRequestParams> requestContext, string queryId, string entryId)
    {
        var uri = requestContext.Params?.Uri ?? $"drasi://{_reactionName}/queries/{queryId}/entries/{entryId}";
        _logger.LogDebug("Reading entry resource: {Uri}", uri);
        
        var resource = _resourceStore.GetResource(uri);
        if (resource == null)
        {
            throw new NotSupportedException($"Entry not found: {entryId} in query: {queryId}");
        }

        if (resource.Content != null)
        {
            var text = resource.Content is JsonElement jsonElement 
                ? jsonElement.GetRawText() 
                : JsonSerializer.Serialize(resource.Content);

            return new TextResourceContents
            {
                Text = text,
                MimeType = resource.MimeType ?? "application/json",
                Uri = uri
            };
        }

        return new TextResourceContents
        {
            Text = "null",
            MimeType = "application/json",
            Uri = uri
        };
    }
}