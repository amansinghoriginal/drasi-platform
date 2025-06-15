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

using Drasi.Reactions.Mcp.Models;

namespace Drasi.Reactions.Mcp.Interfaces;

public interface IMcpResourceStore
{
    Task<IEnumerable<McpResource>> GetAvailableQueriesAsync();
    Task<IEnumerable<McpResource>> GetQueryEntriesAsync(string queryId);
    Task<McpResource?> GetEntryAsync(string queryId, string entryKey);
    Task<McpResource?> GetResourceByUriAsync(string uri);
    Task UpsertResourceAsync(McpResource resource);
    Task DeleteResourceAsync(string uri);
    Task DeleteEntryAsync(string queryId, string entryKey);
    Task<bool> InitializeQueryAsync(string queryId, string keyField, string? description = null);
}