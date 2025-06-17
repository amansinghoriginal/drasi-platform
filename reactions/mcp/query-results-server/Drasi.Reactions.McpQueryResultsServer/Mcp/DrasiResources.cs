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

using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Text.Json;
using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.McpQueryResultsServer.Services;
using Drasi.Reactions.McpQueryResultsServer.Models;

namespace Drasi.Reactions.McpQueryResultsServer.Mcp;

[McpServerResourceType]
public class DrasiResources
{
    private readonly IQueryResultStore _resultStore;
    private readonly IQueryConfigService _configService;
    private readonly ILogger<DrasiResources> _logger;
    
    public DrasiResources(
        IQueryResultStore resultStore,
        IQueryConfigService configService,
        ILogger<DrasiResources> logger)
    {
        _resultStore = resultStore;
        _configService = configService;
        _logger = logger;
    }
    
    [McpServerResource(UriTemplate = "drasi://queries/{queryId}", Name = "Drasi Query")]
    [Description("Get all entries for a Drasi continuous query")]
    public async Task<TextResourceContents> GetQueryEntries(
        string queryId,
        CancellationToken cancellationToken)
    {
        
        try
        {
            // Get query configuration
            var config = _configService.GetQueryConfig<QueryConfig>(queryId);
            if (config == null)
            {
                throw new McpException($"Query not found: {queryId}", McpErrorCode.InvalidParams);
            }
            
            // Get entry keys from store
            var entryKeys = await _resultStore.GetQueryEntriesAsync(queryId);
            var entries = entryKeys
                .Select(key => $"drasi://entries/{queryId}/{key}")
                .ToList();
            
            var response = new
            {
                queryId,
                description = config.Description,
                contentType = config.ResourceContentType,
                entryCount = entries.Count,
                entries
            };
            
            _logger.LogDebug("Retrieved {Count} entries for query {QueryId}", entries.Count, queryId);
            
            return new TextResourceContents
            {
                Uri = $"drasi://queries/{queryId}",
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(response, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                })
            };
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving query entries for {QueryId}", queryId);
            throw new McpException("Internal error", McpErrorCode.InternalError);
        }
    }
    
    [McpServerResource(UriTemplate = "drasi://entries/{queryId}/{entryKey}", Name = "Drasi Entry")]
    [Description("Get a specific entry from a Drasi query result")]
    public async Task<TextResourceContents> GetEntry(
        string queryId,
        string entryKey,
        CancellationToken cancellationToken)
    {
        
        try
        {
            var config = _configService.GetQueryConfig<QueryConfig>(queryId);
            if (config == null)
            {
                throw new McpException($"Query not found: {queryId}", McpErrorCode.InvalidParams);
            }
            
            var entry = await _resultStore.GetEntryAsync(queryId, entryKey);
            if (entry == null)
            {
                throw new McpException($"Entry not found: {entryKey}", McpErrorCode.InvalidParams);
            }
            
            _logger.LogDebug("Retrieved entry {EntryKey} for query {QueryId}", entryKey, queryId);
            
            return new TextResourceContents
            {
                Uri = $"drasi://entries/{queryId}/{entryKey}",
                MimeType = config.ResourceContentType,
                Text = entry.Value.GetRawText()
            };
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving entry {EntryKey} for query {QueryId}", entryKey, queryId);
            throw new McpException("Internal error", McpErrorCode.InternalError);
        }
    }
}