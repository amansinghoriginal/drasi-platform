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

using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.McpQueryResultsServer.Services;
using Drasi.Reactions.McpQueryResultsServer.Models;

namespace Drasi.Reactions.McpQueryResultsServer.Tests;

public class QueryResultStoreTests
{
    private readonly Mock<ILogger<QueryResultStore>> _mockLogger;
    private readonly QueryResultStore _store;

    public QueryResultStoreTests()
    {
        _mockLogger = new Mock<ILogger<QueryResultStore>>();
        _store = new QueryResultStore(_mockLogger.Object);
    }

    [Fact]
    public async Task GetQueryEntriesAsync_WithNoData_ReturnsEmptyList()
    {
        // Act
        var result = await _store.GetQueryEntriesAsync("non-existent-query");

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetEntryAsync_WithNoData_ReturnsNull()
    {
        // Act
        var result = await _store.GetEntryAsync("non-existent-query", "non-existent-key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_AddedResults_AddsEntries()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } },
                new Dictionary<string, object?> { { "id", "2" }, { "name", "Product 2" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);
        var entry1 = await _store.GetEntryAsync(queryId, "1");
        var entry2 = await _store.GetEntryAsync(queryId, "2");

        // Assert
        Assert.Equal(2, entries.Count());
        Assert.Contains("1", entries);
        Assert.Contains("2", entries);
        Assert.NotNull(entry1);
        Assert.NotNull(entry2);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_DeletedResults_RemovesEntries()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        
        // First add some entries
        var addEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } },
                new Dictionary<string, object?> { { "id", "2" }, { "name", "Product 2" } }
            }
        };
        await _store.ApplyChangeEventAsync(addEvent, config);

        // Then delete one
        var deleteEvent = new ChangeEvent
        {
            QueryId = queryId,
            DeletedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(deleteEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);
        var entry1 = await _store.GetEntryAsync(queryId, "1");
        var entry2 = await _store.GetEntryAsync(queryId, "2");

        // Assert
        Assert.Single(entries);
        Assert.Contains("2", entries);
        Assert.Null(entry1); // Deleted
        Assert.NotNull(entry2); // Still exists
    }

    [Fact]
    public async Task ApplyChangeEventAsync_UpdatedResults_UpdatesEntries()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        
        // First add an entry
        var addEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Original Product" }, { "price", 100 } }
            }
        };
        await _store.ApplyChangeEventAsync(addEvent, config);

        // Then update it
        var updateEvent = new ChangeEvent
        {
            QueryId = queryId,
            UpdatedResults = new[]
            {
                new UpdatedResultElement
                {
                    After = new Dictionary<string, object?> { { "id", "1" }, { "name", "Updated Product" }, { "price", 150 } }
                }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(updateEvent, config);
        var entry = await _store.GetEntryAsync(queryId, "1");

        // Assert
        Assert.NotNull(entry);
        var entryObj = JsonSerializer.Deserialize<JsonElement>(entry.Value);
        Assert.Equal("Updated Product", entryObj.GetProperty("name").GetString());
        Assert.Equal(150, entryObj.GetProperty("price").GetInt32());
    }

    [Fact]
    public async Task DeletedResults_RemovesAllEntriesWhenAllDeleted()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        
        // First add entries
        var addEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } },
                new Dictionary<string, object?> { { "id", "2" }, { "name", "Product 2" } }
            }
        };
        await _store.ApplyChangeEventAsync(addEvent, config);
        
        // Then delete all of them
        var deleteEvent = new ChangeEvent
        {
            QueryId = queryId,
            DeletedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } },
                new Dictionary<string, object?> { { "id", "2" }, { "name", "Product 2" } }
            }
        };
        
        // Act
        await _store.ApplyChangeEventAsync(deleteEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);

        // Assert
        Assert.Empty(entries);
    }
}