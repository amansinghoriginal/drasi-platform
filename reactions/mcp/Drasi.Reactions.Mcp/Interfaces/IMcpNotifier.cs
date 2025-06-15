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

namespace Drasi.Reactions.Mcp.Interfaces;

/// <summary>
/// Interface for sending MCP notifications to connected clients
/// </summary>
public interface IMcpNotifier
{
    /// <summary>
    /// Notify clients that a resource has been updated
    /// </summary>
    Task NotifyResourceUpdatedAsync(string uri);
    
    /// <summary>
    /// Notify clients that a resource has been created
    /// </summary>
    Task NotifyResourceCreatedAsync(string uri);
    
    /// <summary>
    /// Notify clients that a resource has been deleted
    /// </summary>
    Task NotifyResourceDeletedAsync(string uri);
    
    /// <summary>
    /// Notify clients that multiple resources have changed
    /// </summary>
    Task NotifyResourcesChangedAsync(IEnumerable<string> uris);
    
    /// <summary>
    /// Notify that a resource has been subscribed to
    /// </summary>
    Task NotifyResourceSubscribedAsync(string uri);
    
    /// <summary>
    /// Notify that a resource has been unsubscribed from
    /// </summary>
    Task NotifyResourceUnsubscribedAsync(string uri);
}