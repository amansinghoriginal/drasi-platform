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
using Drasi.Reactions.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Drasi.Reactions.Mcp.Tests;

public class InMemoryMcpResourceStoreTests
{
    private readonly Mock<ILogger<InMemoryMcpResourceStore>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly InMemoryMcpResourceStore _store;

    public InMemoryMcpResourceStoreTests()
    {
        _mockLogger = new Mock<ILogger<InMemoryMcpResourceStore>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        _mockConfiguration.Setup(c => c["REACTION_NAME"]).Returns("test-reaction");
        
        _store = new InMemoryMcpResourceStore(_mockLogger.Object, _mockConfiguration.Object);
    }

    [Fact]
    public async Task InitializeQueryAsync_CreatesQueryMetadata()
    {
        // Act
        var result = await _store.InitializeQueryAsync("query1", "id", "Test Query");

        // Assert
        result.Should().BeTrue();
        
        var queries = await _store.GetAvailableQueriesAsync();
        queries.Should().HaveCount(1);
        queries.First().QueryId.Should().Be("query1");
        queries.First().Uri.Should().Be("drasi://test-reaction/queries/query1");
    }

    [Fact]
    public async Task UpsertResourceAsync_CreatesNewResource()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id");
        
        var resource = new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/123",
            QueryId = "query1",
            EntryKey = "123",
            Data = new { id = "123", name = "Test" }
        };

        // Act
        await _store.UpsertResourceAsync(resource);

        // Assert
        var retrieved = await _store.GetEntryAsync("query1", "123");
        retrieved.Should().NotBeNull();
        retrieved!.Uri.Should().Be(resource.Uri);
        retrieved.Data.Should().BeEquivalentTo(resource.Data);
    }

    [Fact]
    public async Task UpsertResourceAsync_UpdatesExistingResource()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id");
        
        var resource1 = new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/123",
            QueryId = "query1",
            EntryKey = "123",
            Data = new { id = "123", name = "Original" }
        };

        var resource2 = new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/123",
            QueryId = "query1",
            EntryKey = "123",
            Data = new { id = "123", name = "Updated" }
        };

        // Act
        await _store.UpsertResourceAsync(resource1);
        await _store.UpsertResourceAsync(resource2);

        // Assert
        var retrieved = await _store.GetEntryAsync("query1", "123");
        retrieved!.Data.Should().BeEquivalentTo(resource2.Data);
    }

    [Fact]
    public async Task GetQueryEntriesAsync_ReturnsEntriesForQuery()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id");
        await _store.InitializeQueryAsync("query2", "id");
        
        await _store.UpsertResourceAsync(new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/1",
            QueryId = "query1",
            EntryKey = "1"
        });
        
        await _store.UpsertResourceAsync(new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/2",
            QueryId = "query1",
            EntryKey = "2"
        });
        
        await _store.UpsertResourceAsync(new McpResource
        {
            Uri = "drasi://test-reaction/entries/query2/3",
            QueryId = "query2",
            EntryKey = "3"
        });

        // Act
        var query1Entries = await _store.GetQueryEntriesAsync("query1");
        var query2Entries = await _store.GetQueryEntriesAsync("query2");

        // Assert
        query1Entries.Should().HaveCount(2);
        query1Entries.Select(e => e.EntryKey).Should().BeEquivalentTo(new[] { "1", "2" });
        
        query2Entries.Should().HaveCount(1);
        query2Entries.First().EntryKey.Should().Be("3");
    }

    [Fact]
    public async Task DeleteEntryAsync_RemovesResource()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id");
        
        await _store.UpsertResourceAsync(new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/123",
            QueryId = "query1",
            EntryKey = "123"
        });

        // Act
        await _store.DeleteEntryAsync("query1", "123");

        // Assert
        var retrieved = await _store.GetEntryAsync("query1", "123");
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetResourceByUriAsync_ReturnsCorrectResource()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id");
        
        var resource = new McpResource
        {
            Uri = "drasi://test-reaction/entries/query1/123",
            QueryId = "query1",
            EntryKey = "123",
            Data = new { test = "data" }
        };
        
        await _store.UpsertResourceAsync(resource);

        // Act
        var retrieved = await _store.GetResourceByUriAsync(resource.Uri);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Uri.Should().Be(resource.Uri);
        retrieved.Data.Should().BeEquivalentTo(resource.Data);
    }

    [Fact]
    public async Task GetResourceByUriAsync_ReturnsNullForNonexistent()
    {
        // Act
        var retrieved = await _store.GetResourceByUriAsync("drasi://test-reaction/entries/query1/nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task GetAvailableQueriesAsync_ReturnsAllQueries()
    {
        // Arrange
        await _store.InitializeQueryAsync("query1", "id", "Query One");
        await _store.InitializeQueryAsync("query2", "key", "Query Two");
        await _store.InitializeQueryAsync("query3", "identifier");

        // Act
        var queries = await _store.GetAvailableQueriesAsync();

        // Assert
        queries.Should().HaveCount(3);
        queries.Select(q => q.QueryId).Should().BeEquivalentTo(new[] { "query1", "query2", "query3" });
        
        var query1 = queries.First(q => q.QueryId == "query1");
        query1.Data.Should().NotBeNull();
    }
}