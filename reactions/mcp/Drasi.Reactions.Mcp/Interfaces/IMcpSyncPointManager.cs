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
/// Manages sync points for queries to prevent duplicate processing
/// </summary>
public interface IMcpSyncPointManager
{
    /// <summary>
    /// Gets the current sync point for a query
    /// </summary>
    /// <param name="queryId">The query ID</param>
    /// <returns>The sync point sequence number, or null if not initialized</returns>
    long? GetSyncPoint(string queryId);

    /// <summary>
    /// Updates the sync point for a query
    /// </summary>
    /// <param name="queryId">The query ID</param>
    /// <param name="sequence">The new sequence number</param>
    Task UpdateSyncPointAsync(string queryId, long sequence);

    /// <summary>
    /// Initializes a sync point for a query
    /// </summary>
    /// <param name="queryId">The query ID</param>
    /// <param name="sequence">The initial sequence number</param>
    Task InitializeSyncPointAsync(string queryId, long sequence);
}