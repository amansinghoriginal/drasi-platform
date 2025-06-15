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
using Drasi.Reactions.Mcp.Models;
using Drasi.Reactions.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Drasi.Reactions.Mcp.Tests;

public class McpResourceProviderTests
{
    private readonly Mock<IMcpResourceStore> _mockResourceStore;
    private readonly Mock<ILogger<McpResourceProvider>> _mockLogger;
    private readonly McpResourceProvider _provider;

    public McpResourceProviderTests()
    {
        _mockResourceStore = new Mock<IMcpResourceStore>();
        _mockLogger = new Mock<ILogger<McpResourceProvider>>();
        _provider = new McpResourceProvider(_mockResourceStore.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task ListResources_ReturnsOnlyQueryResources()
    {
        // Arrange
        var queries = new List<McpResource>
        {
            new McpResource
            {
                Uri = "drasi://test-reaction/queries/query1",
                QueryId = "query1",
                Data = new { description = "Test Query 1" },
                ContentType = "application/json"
            },
            new McpResource
            {
                Uri = "drasi://test-reaction/queries/query2",
                QueryId = "query2",
                Data = new { description = "Test Query 2" },
                ContentType = "application/json"
            }
        };

        var entries = new List<McpResource>
        {
            new McpResource
            {
                Uri = "drasi://test-reaction/entries/query1/entry1",
                QueryId = "query1",
                EntryKey = "entry1",
                ContentType = "application/json"
            },
            new McpResource
            {
                Uri = "drasi://test-reaction/entries/query1/entry2",
                QueryId = "query1",
                EntryKey = "entry2",
                ContentType = "application/json"
            }
        };

        _mockResourceStore.Setup(x => x.GetAvailableQueriesAsync())
            .ReturnsAsync(queries);

        // Act
        var resources = await _provider.ListResources();

        // Assert
        resources.Should().HaveCount(2); // Only query resources, not entries
        
        var queryResource1 = resources.First(r => r.Name == "Query: query1");
        queryResource1.Uri.Should().Be("drasi://test-reaction/queries/query1");
        queryResource1.MimeType.Should().Be("application/json");
        
        var queryResource2 = resources.First(r => r.Name == "Query: query2");
        queryResource2.Uri.Should().Be("drasi://test-reaction/queries/query2");
        
        // Should not include any entry resources
        var entryResources = resources.Where(r => r.Name.StartsWith("Entry:")).ToList();
        entryResources.Should().HaveCount(0);
    }

    [Fact]
    public async Task ReadResource_WithQueryUri_ReturnsEntryList()
    {
        // Arrange
        var queryUri = "drasi://test-reaction/queries/query1";
        var entries = new List<McpResource>
        {
            new McpResource { Uri = "drasi://test-reaction/entries/query1/1" },
            new McpResource { Uri = "drasi://test-reaction/entries/query1/2" }
        };

        _mockResourceStore.Setup(x => x.GetQueryEntriesAsync("query1"))
            .ReturnsAsync(entries);

        // Act
        var content = await _provider.ReadResource(queryUri);

        // Assert
        content.Should().NotBeNull();
        content!.Uri.Should().Be(queryUri);
        content.MimeType.Should().Be("application/json");
        
        var data = JsonDocument.Parse(content.Text).RootElement;
        data.GetProperty("queryId").GetString().Should().Be("query1");
        data.GetProperty("entryCount").GetInt32().Should().Be(2);
        data.GetProperty("entries").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ReadResource_WithEntryUri_ReturnsEntryData()
    {
        // Arrange
        var entryUri = "drasi://test-reaction/entries/query1/123";
        var resource = new McpResource
        {
            Uri = entryUri,
            QueryId = "query1",
            EntryKey = "123",
            Data = new { id = "123", name = "Test Entry" },
            ContentType = "application/json"
        };

        _mockResourceStore.Setup(x => x.GetResourceByUriAsync(entryUri))
            .ReturnsAsync(resource);

        // Act
        var content = await _provider.ReadResource(entryUri);

        // Assert
        content.Should().NotBeNull();
        content!.Uri.Should().Be(entryUri);
        content.MimeType.Should().Be("application/json");
        
        var data = JsonDocument.Parse(content.Text).RootElement;
        data.GetProperty("id").GetString().Should().Be("123");
        data.GetProperty("name").GetString().Should().Be("Test Entry");
    }

    [Fact]
    public async Task ReadResource_WithInvalidUri_ReturnsNull()
    {
        // Arrange
        var invalidUri = "invalid://uri";

        // Act
        var content = await _provider.ReadResource(invalidUri);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public async Task ReadResource_WithNonexistentResource_ReturnsNull()
    {
        // Arrange
        var uri = "drasi://test-reaction/entries/query1/nonexistent";
        _mockResourceStore.Setup(x => x.GetResourceByUriAsync(uri))
            .ReturnsAsync((McpResource?)null);

        // Act
        var content = await _provider.ReadResource(uri);

        // Assert
        content.Should().BeNull();
    }

    [Fact]
    public async Task ReadResource_HandlesEntryKeysWithSlashes()
    {
        // Arrange
        var entryUri = "drasi://test-reaction/entries/query1/path/to/key";
        var resource = new McpResource
        {
            Uri = entryUri,
            QueryId = "query1",
            EntryKey = "path/to/key",
            Data = new { complex = "key" },
            ContentType = "application/json"
        };

        _mockResourceStore.Setup(x => x.GetResourceByUriAsync(entryUri))
            .ReturnsAsync(resource);

        // Act
        var content = await _provider.ReadResource(entryUri);

        // Assert
        content.Should().NotBeNull();
        content!.Uri.Should().Be(entryUri);
    }
}