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
using ModelContextProtocol;
using Drasi.Reactions.McpQueryResultsServer.Mcp;
using Drasi.Reactions.McpQueryResultsServer.Services;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reaction.SDK.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests.Mcp;

public class DrasiResourcesTests
{
    private readonly Mock<IQueryResultStore> _mockResultStore;
    private readonly Mock<IQueryConfigService> _mockConfigService;
    private readonly Mock<ILogger<DrasiResources>> _mockLogger;
    private readonly DrasiResources _resources;

    public DrasiResourcesTests()
    {
        _mockResultStore = new Mock<IQueryResultStore>();
        _mockConfigService = new Mock<IQueryConfigService>();
        _mockLogger = new Mock<ILogger<DrasiResources>>();
        _resources = new DrasiResources(_mockResultStore.Object, _mockConfigService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetQueryEntries_WithValidQuery_ReturnsQueryData()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig
        {
            KeyField = "id",
            Description = "Test Query Description",
            ResourceContentType = "application/json"
        };
        var entryKeys = new[] { "key1", "key2", "key3" };

        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetQueryEntriesAsync(queryId)).ReturnsAsync(entryKeys);

        // Act
        var result = await _resources.GetQueryEntries(queryId, CancellationToken.None);

        // Assert
        Assert.Equal($"drasi://queries/{queryId}", result.Uri);
        Assert.Equal("application/json", result.MimeType);
        
        var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(result.Text);
        Assert.Equal(queryId, responseData["queryId"].ToString());
        Assert.Equal("Test Query Description", responseData["description"].ToString());
        Assert.Equal("application/json", responseData["contentType"].ToString());
        Assert.Equal(3, ((JsonElement)responseData["entryCount"]).GetInt32());
        
        var entries = JsonSerializer.Deserialize<string[]>(responseData["entries"].ToString());
        Assert.Contains($"drasi://entries/{queryId}/key1", entries);
        Assert.Contains($"drasi://entries/{queryId}/key2", entries);
        Assert.Contains($"drasi://entries/{queryId}/key3", entries);
    }

    [Fact]
    public async Task GetQueryEntries_WithNonExistentQuery_ThrowsMcpException()
    {
        // Arrange
        var queryId = "non-existent";
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns((QueryConfig?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(() => 
            _resources.GetQueryEntries(queryId, CancellationToken.None));
        
        // Verify exception message instead of code
        Assert.Contains("Query not found", exception.Message);
    }

    [Fact]
    public async Task GetQueryEntries_WithEmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var queryId = "empty-query";
        var config = new QueryConfig { KeyField = "id" };
        
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetQueryEntriesAsync(queryId)).ReturnsAsync(Array.Empty<string>());

        // Act
        var result = await _resources.GetQueryEntries(queryId, CancellationToken.None);

        // Assert
        var responseData = JsonSerializer.Deserialize<Dictionary<string, object>>(result.Text);
        Assert.Equal(0, ((JsonElement)responseData["entryCount"]).GetInt32());
        
        var entries = JsonSerializer.Deserialize<string[]>(responseData["entries"].ToString());
        Assert.Empty(entries);
    }

    [Fact]
    public async Task GetQueryEntries_WhenExceptionThrown_LogsAndThrowsMcpException()
    {
        // Arrange
        var queryId = "error-query";
        var config = new QueryConfig { KeyField = "id" };
        var exception = new InvalidOperationException("Database error");
        
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetQueryEntriesAsync(queryId)).ThrowsAsync(exception);

        // Act & Assert
        var mcpException = await Assert.ThrowsAsync<McpException>(() => 
            _resources.GetQueryEntries(queryId, CancellationToken.None));
        
        // Verify exception message instead of code
        Assert.Equal("Internal error", mcpException.Message);
        
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error retrieving query entries")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEntry_WithValidEntry_ReturnsEntryData()
    {
        // Arrange
        var queryId = "test-query";
        var entryKey = "entry1";
        var config = new QueryConfig
        {
            KeyField = "id",
            ResourceContentType = "application/json"
        };
        var entryData = JsonDocument.Parse(@"{""id"": ""entry1"", ""name"": ""Test Entry"", ""value"": 42}");

        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetEntryAsync(queryId, entryKey)).ReturnsAsync(entryData.RootElement);

        // Act
        var result = await _resources.GetEntry(queryId, entryKey, CancellationToken.None);

        // Assert
        Assert.Equal($"drasi://entries/{queryId}/{entryKey}", result.Uri);
        Assert.Equal("application/json", result.MimeType);
        Assert.Equal(entryData.RootElement.GetRawText(), result.Text);
    }

    [Fact]
    public async Task GetEntry_WithNonExistentQuery_ThrowsMcpException()
    {
        // Arrange
        var queryId = "non-existent";
        var entryKey = "key1";
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns((QueryConfig?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(() => 
            _resources.GetEntry(queryId, entryKey, CancellationToken.None));
        
        // Verify exception message instead of code
        Assert.Contains("Query not found", exception.Message);
    }

    [Fact]
    public async Task GetEntry_WithNonExistentEntry_ThrowsMcpException()
    {
        // Arrange
        var queryId = "test-query";
        var entryKey = "non-existent";
        var config = new QueryConfig { KeyField = "id" };
        
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetEntryAsync(queryId, entryKey)).ReturnsAsync((JsonElement?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<McpException>(() => 
            _resources.GetEntry(queryId, entryKey, CancellationToken.None));
        
        // Verify exception message instead of code
        Assert.Contains("Entry not found", exception.Message);
    }

    [Fact]
    public async Task GetEntry_WithDifferentContentType_ReturnsCorrectMimeType()
    {
        // Arrange
        var queryId = "test-query";
        var entryKey = "entry1";
        var config = new QueryConfig
        {
            KeyField = "id",
            ResourceContentType = "text/plain"
        };
        var entryData = JsonDocument.Parse(@"{""id"": ""entry1"", ""text"": ""Plain text content""}");

        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetEntryAsync(queryId, entryKey)).ReturnsAsync(entryData.RootElement);

        // Act
        var result = await _resources.GetEntry(queryId, entryKey, CancellationToken.None);

        // Assert
        Assert.Equal("text/plain", result.MimeType);
    }

    [Fact]
    public async Task GetEntry_WhenExceptionThrown_LogsAndThrowsMcpException()
    {
        // Arrange
        var queryId = "test-query";
        var entryKey = "entry1";
        var config = new QueryConfig { KeyField = "id" };
        var exception = new InvalidOperationException("Database error");
        
        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetEntryAsync(queryId, entryKey)).ThrowsAsync(exception);

        // Act & Assert
        var mcpException = await Assert.ThrowsAsync<McpException>(() => 
            _resources.GetEntry(queryId, entryKey, CancellationToken.None));
        
        // Verify exception message instead of code
        Assert.Equal("Internal error", mcpException.Message);
        
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error retrieving entry")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetQueryEntries_LogsDebugInfo()
    {
        // Arrange
        var queryId = "test-query";
        var config = new QueryConfig { KeyField = "id" };
        var entryKeys = new[] { "key1", "key2" };

        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetQueryEntriesAsync(queryId)).ReturnsAsync(entryKeys);

        // Act
        await _resources.GetQueryEntries(queryId, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Retrieved 2 entries for query")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEntry_LogsDebugInfo()
    {
        // Arrange
        var queryId = "test-query";
        var entryKey = "entry1";
        var config = new QueryConfig { KeyField = "id" };
        var entryData = JsonDocument.Parse(@"{""id"": ""entry1""}");

        _mockConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryId)).Returns(config);
        _mockResultStore.Setup(x => x.GetEntryAsync(queryId, entryKey)).ReturnsAsync(entryData.RootElement);

        // Act
        await _resources.GetEntry(queryId, entryKey, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Retrieved entry")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}