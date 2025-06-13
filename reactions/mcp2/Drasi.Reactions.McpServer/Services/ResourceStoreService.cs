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

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Drasi.Reactions.McpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Drasi.Reactions.McpServer.Services;

public interface IResourceStoreService
{
    // Core operations
    void UpdateEntry(string queryId, string key, JsonElement data, QueryConfig config);
    void RemoveEntry(string queryId, string key);
    McpResource? GetResource(string uri);
    
    // Resource discovery
    List<McpResourceMetadata> ListAllResources();
    List<McpResourceMetadata> GetQueryEntries(string queryId);
    List<McpResourceMetadata> ListQueryResources(); // Add this
    
    // Subscriptions
    void Subscribe(string uri, string clientId);
    void Unsubscribe(string uri, string clientId);
    List<string> GetSubscribers(string uri);
    
    // Events
    event EventHandler<ResourceChangedEventArgs>? ResourceChanged;
    event EventHandler<ResourceListChangedEventArgs>? ResourceListChanged;
}

public class ResourceChangedEventArgs : EventArgs
{
    public string Uri { get; set; } = string.Empty;
    public string? OldUri { get; set; }
    public ChangeType Type { get; set; }
}

public enum ChangeType
{
    Created,
    Updated,
    Deleted
}

public class ResourceListChangedEventArgs : EventArgs
{
    public List<string> AddedUris { get; set; } = new();
    public List<string> RemovedUris { get; set; } = new();
}

public class ResourceStoreService : IResourceStoreService
{
    private readonly ConcurrentDictionary<string, McpResource> _resources = new();
    private readonly ConcurrentDictionary<string, QueryConfig> _queryConfigs = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _subscriptions = new();
    private readonly string _reactionName;
    private readonly ILogger<ResourceStoreService> _logger;
    private readonly object _subscriptionLock = new();
    
    public event EventHandler<ResourceChangedEventArgs>? ResourceChanged;
    public event EventHandler<ResourceListChangedEventArgs>? ResourceListChanged;

    public ResourceStoreService(
        IConfiguration configuration,
        ILogger<ResourceStoreService> logger)
    {
        _reactionName = configuration["REACTION_NAME"] ?? "mcp-server";
        _logger = logger;
    }

    public void UpdateEntry(string queryId, string key, JsonElement data, QueryConfig config)
    {
        _queryConfigs[queryId] = config;
        
        // Corrected hierarchical entry URI
        var entryUri = $"drasi://{_reactionName}/queries/{queryId}/entries/{key}";
        var queryUri = $"drasi://{_reactionName}/queries/{queryId}";
        
        // Ensure query resource exists
        if (!_resources.ContainsKey(queryUri))
        {
            var queryResource = new McpResource
            {
                Uri = queryUri,
                Name = queryId,
                Description = config.Description ?? $"Drasi query {queryId}",
                MimeType = "application/json",
                Type = ResourceType.Query,
                LastModified = DateTime.UtcNow
            };
            _resources[queryUri] = queryResource;
            
            ResourceListChanged?.Invoke(this, new ResourceListChangedEventArgs
            {
                AddedUris = [queryUri]
            });
        }
        
        // Update or create entry resource
        var isNew = !_resources.ContainsKey(entryUri);
        
        var entryResource = new McpResource
        {
            Uri = entryUri,
            Name = $"{queryId}/{key}",
            Description = $"Entry {key} from query {queryId}",
            MimeType = config.ResourceContentType,
            Content = data,
            Type = ResourceType.Entry,
            LastModified = DateTime.UtcNow
        };
        
        _resources[entryUri] = entryResource;
        
        // Notify subscribers
        ResourceChanged?.Invoke(this, new ResourceChangedEventArgs
        {
            Uri = entryUri,
            Type = isNew ? ChangeType.Created : ChangeType.Updated
        });
        
        if (isNew)
        {
            // Query content changed (new entry added)
            ResourceChanged?.Invoke(this, new ResourceChangedEventArgs
            {
                Uri = queryUri,
                Type = ChangeType.Updated
            });
            
            ResourceListChanged?.Invoke(this, new ResourceListChangedEventArgs
            {
                AddedUris = [entryUri]
            });
        }
        
        _logger.LogDebug("{Action} resource {Uri}", isNew ? "Created" : "Updated", entryUri);
    }

    public void RemoveEntry(string queryId, string key)
    {
        // Corrected hierarchical entry URI
        var entryUri = $"drasi://{_reactionName}/queries/{queryId}/entries/{key}";
        var queryUri = $"drasi://{_reactionName}/queries/{queryId}";
        
        if (_resources.TryRemove(entryUri, out _))
        {
            // Notify about entry removal
            ResourceChanged?.Invoke(this, new ResourceChangedEventArgs
            {
                Uri = entryUri,
                Type = ChangeType.Deleted
            });
            
            // Notify about query content change
            ResourceChanged?.Invoke(this, new ResourceChangedEventArgs
            {
                Uri = queryUri,
                Type = ChangeType.Updated
            });
            
            ResourceListChanged?.Invoke(this, new ResourceListChangedEventArgs
            {
                RemovedUris = [entryUri]
            });
            
            _logger.LogDebug("Removed resource {Uri}", entryUri);
        }
    }

    public McpResource? GetResource(string uri)
    {
        if (_resources.TryGetValue(uri, out var resource))
        {
            // For query resources, generate the content dynamically
            if (resource.Type == ResourceType.Query)
            {
                var queryId = ExtractQueryId(uri);
                if (queryId != null)
                {
                    // Clone the resource and set the content
                    resource = new McpResource
                    {
                        Uri = resource.Uri,
                        Name = resource.Name,
                        Description = resource.Description,
                        MimeType = resource.MimeType,
                        Type = resource.Type,
                        LastModified = resource.LastModified,
                        Content = GetQueryEntries(queryId)
                    };
                }
            }
            return resource;
        }
        return null;
    }

    public List<McpResourceMetadata> ListAllResources()
    {
        return _resources.Values
            .Select(r => new McpResourceMetadata
            {
                Uri = r.Uri,
                Name = r.Name,
                Description = r.Description,
                MimeType = r.MimeType
            })
            .OrderBy(r => r.Uri)
            .ToList();
    }

    public List<McpResourceMetadata> GetQueryEntries(string queryId)
    {
        // Corrected prefix for hierarchical entries
        var prefix = $"drasi://{_reactionName}/queries/{queryId}/entries/";
        return _resources.Values
            .Where(r => r.Type == ResourceType.Entry && r.Uri.StartsWith(prefix))
            .Select(r => new McpResourceMetadata
            {
                Uri = r.Uri,
                Name = r.Name,
                Description = r.Description,
                MimeType = r.MimeType
            })
            .OrderBy(r => r.Uri)
            .ToList();
    }

    // New method to specifically list query resources
    public List<McpResourceMetadata> ListQueryResources()
    {
        return _resources.Values
            .Where(r => r.Type == ResourceType.Query)
            .Select(r => new McpResourceMetadata
            {
                Uri = r.Uri,
                Name = r.Name,
                Description = r.Description,
                MimeType = r.MimeType
            })
            .OrderBy(r => r.Uri)
            .ToList();
    }

    public void Subscribe(string uri, string clientId)
    {
        lock (_subscriptionLock)
        {
            var subscribers = _subscriptions.GetOrAdd(uri, _ => new HashSet<string>());
            subscribers.Add(clientId);
        }
        _logger.LogDebug("Client {ClientId} subscribed to {Uri}", clientId, uri);
    }

    public void Unsubscribe(string uri, string clientId)
    {
        lock (_subscriptionLock)
        {
            if (_subscriptions.TryGetValue(uri, out var subscribers))
            {
                subscribers.Remove(clientId);
                if (subscribers.Count == 0)
                {
                    _subscriptions.TryRemove(uri, out _);
                }
            }
        }
        _logger.LogDebug("Client {ClientId} unsubscribed from {Uri}", clientId, uri);
    }

    public List<string> GetSubscribers(string uri)
    {
        lock (_subscriptionLock)
        {
            if (_subscriptions.TryGetValue(uri, out var subscribers))
            {
                return subscribers.ToList();
            }
        }
        return [];
    }

    private string? ExtractQueryId(string uri)
    {
        var match = Regex.Match(
            uri, 
            @"drasi://[^/]+/queries/([^/]+)$"
        );
        return match.Success ? match.Groups[1].Value : null;
    }
}