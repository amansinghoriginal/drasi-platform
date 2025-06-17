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

namespace Drasi.Reactions.McpQueryResultsServer.Tests.Services;

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
        Assert.Equal("Product 1", JsonSerializer.Deserialize<Dictionary<string, object>>(entry1.Value.GetRawText())["name"].ToString());
        Assert.Equal("Product 2", JsonSerializer.Deserialize<Dictionary<string, object>>(entry2.Value.GetRawText())["name"].ToString());
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
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(entry.Value.GetRawText());
        Assert.Equal("Updated Product", data["name"].ToString());
        Assert.Equal("150", data["price"].ToString());
    }

    [Fact]
    public async Task ApplyChangeEventAsync_ComplexKeyValues_HandlesCorrectly()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", 123 }, { "name", "Numeric ID" } }, // Numeric key
                new Dictionary<string, object?> { { "id", "special-key/with/slashes" }, { "name", "Special chars" } } // String with special chars
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);
        var numericEntry = await _store.GetEntryAsync(queryId, "123");
        var specialEntry = await _store.GetEntryAsync(queryId, "special-key/with/slashes");

        // Assert
        Assert.Equal(2, entries.Count());
        Assert.NotNull(numericEntry);
        Assert.NotNull(specialEntry);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_MissingKeyField_SkipsEntry()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "name", "No ID Field" } }, // Missing 'id'
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Has ID" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);

        // Assert
        Assert.Single(entries); // Only the entry with ID should be added
        Assert.Contains("1", entries);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_NullKeyValue_SkipsEntry()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", null }, { "name", "Null ID" } },
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Valid ID" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);

        // Assert
        Assert.Single(entries); // Only the entry with valid ID should be added
        Assert.Contains("1", entries);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_EmptyStringKey_SkipsEntry()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "" }, { "name", "Empty ID" } },
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Valid ID" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);
        var entries = await _store.GetQueryEntriesAsync(queryId);

        // Assert
        Assert.Single(entries); // Only the entry with valid ID should be added
        Assert.Contains("1", entries);
    }

    [Fact]
    public async Task ApplyChangeEventAsync_MultipleQueries_MaintainsSeparation()
    {
        // Arrange
        var config = new QueryConfig { KeyField = "id" };
        var event1 = new ChangeEvent
        {
            QueryId = "query1",
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Query1 Product" } }
            }
        };
        var event2 = new ChangeEvent
        {
            QueryId = "query2",
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Query2 Product" } }
            }
        };

        // Act
        await _store.ApplyChangeEventAsync(event1, config);
        await _store.ApplyChangeEventAsync(event2, config);
        var query1Entries = await _store.GetQueryEntriesAsync("query1");
        var query2Entries = await _store.GetQueryEntriesAsync("query2");
        var query1Entry = await _store.GetEntryAsync("query1", "1");
        var query2Entry = await _store.GetEntryAsync("query2", "1");

        // Assert
        Assert.Single(query1Entries);
        Assert.Single(query2Entries);
        Assert.NotNull(query1Entry);
        Assert.NotNull(query2Entry);
        
        var data1 = JsonSerializer.Deserialize<Dictionary<string, object>>(query1Entry.Value.GetRawText());
        var data2 = JsonSerializer.Deserialize<Dictionary<string, object>>(query2Entry.Value.GetRawText());
        Assert.Equal("Query1 Product", data1["name"].ToString());
        Assert.Equal("Query2 Product", data2["name"].ToString());
    }

    [Fact]
    public async Task ApplyChangeEventAsync_LogsProcessingInfo()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[] { new Dictionary<string, object?> { { "id", "1" }, { "name", "Product" } } },
            UpdatedResults = new[] { new UpdatedResultElement { After = new Dictionary<string, object?> { { "id", "2" }, { "name", "Updated" } } } },
            DeletedResults = new[] { new Dictionary<string, object?> { { "id", "3" }, { "name", "Deleted" } } }
        };

        // Act
        await _store.ApplyChangeEventAsync(changeEvent, config);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Applied changes to query")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}