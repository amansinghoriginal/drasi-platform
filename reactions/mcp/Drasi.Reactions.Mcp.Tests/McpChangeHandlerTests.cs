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

using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.Mcp.Interfaces;
using Drasi.Reactions.Mcp.Models;
using Drasi.Reactions.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace Drasi.Reactions.Mcp.Tests;

public class McpChangeHandlerTests
{
    private readonly Mock<ILogger<McpChangeHandler>> _mockLogger;
    private readonly Mock<IMcpResourceStore> _mockResourceStore;
    private readonly Mock<IMcpSyncPointManager> _mockSyncPointManager;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly McpChangeHandler _handler;

    public McpChangeHandlerTests()
    {
        _mockLogger = new Mock<ILogger<McpChangeHandler>>();
        _mockResourceStore = new Mock<IMcpResourceStore>();
        _mockSyncPointManager = new Mock<IMcpSyncPointManager>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        _mockConfiguration.Setup(c => c["REACTION_NAME"]).Returns("test-reaction");
        
        _handler = new McpChangeHandler(
            _mockLogger.Object, 
            _mockResourceStore.Object, 
            _mockSyncPointManager.Object,
            _mockConfiguration.Object);
    }

    private void SetupInitializedQuery(string queryId, long syncPoint = 0)
    {
        _mockSyncPointManager.Setup(x => x.GetSyncPoint(queryId)).Returns(syncPoint);
    }

    [Fact]
    public async Task HandleChange_WithNullConfig_ThrowsException()
    {
        // Arrange
        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 1
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _handler.HandleChange(changeEvent, null));
        
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(It.IsAny<McpResource>()), Times.Never);
    }

    [Fact]
    public async Task HandleChange_WithUninitializedQuery_ThrowsException()
    {
        // Arrange
        var queryConfig = new McpQueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 1
        };

        // Setup sync point manager to return null (not initialized)
        _mockSyncPointManager.Setup(x => x.GetSyncPoint("test-query")).Returns((long?)null);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _handler.HandleChange(changeEvent, queryConfig));
        
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(It.IsAny<McpResource>()), Times.Never);
    }

    [Fact]
    public async Task HandleChange_WithOldSequence_SkipsProcessing()
    {
        // Arrange
        var queryConfig = new McpQueryConfig { KeyField = "id" };
        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 5,
            AddedResults = new[] { new Dictionary<string, object> { { "id", "123" } } }
        };

        // Setup sync point manager to return a higher sync point
        _mockSyncPointManager.Setup(x => x.GetSyncPoint("test-query")).Returns(10);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert - verify nothing was processed
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(It.IsAny<McpResource>()), Times.Never);
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync(It.IsAny<string>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task HandleChange_ProcessesAddedResults()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "id",
            ResourceContentType = "application/json"
        };

        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            AddedResults = new[]
            {
                new Dictionary<string, object>
                {
                    { "id", "123" },
                    { "name", "Test Item" }
                }
            }
        };

        // Setup initialized query with lower sync point
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(
            It.Is<McpResource>(r => 
                r.Uri == "drasi://test-reaction/entries/test-query/123" &&
                r.QueryId == "test-query" &&
                r.EntryKey == "123" &&
                r.ContentType == "application/json"
            )), Times.Once);
        
        // Verify sync point was updated
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }

    [Fact]
    public async Task HandleChange_ProcessesUpdatedResults()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "id",
            ResourceContentType = "application/json"
        };

        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            UpdatedResults = new[]
            {
                new UpdatedResultElement 
                { 
                    Before = new Dictionary<string, object> { { "id", "123" }, { "name", "Old Name" } },
                    After = new Dictionary<string, object> { { "id", "123" }, { "name", "New Name" } }
                }
            }
        };

        // Setup initialized query
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(
            It.Is<McpResource>(r => 
                r.Uri == "drasi://test-reaction/entries/test-query/123" &&
                r.QueryId == "test-query" &&
                r.EntryKey == "123"
            )), Times.Once);
        
        // Verify sync point was updated
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }

    [Fact]
    public async Task HandleChange_ProcessesDeletedResults()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "id"
        };

        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            DeletedResults = new[]
            {
                new Dictionary<string, object>
                {
                    { "id", "123" },
                    { "name", "Deleted Item" }
                }
            }
        };

        // Setup initialized query
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockResourceStore.Verify(x => x.DeleteEntryAsync("test-query", "123"), Times.Once);
        
        // Verify sync point was updated
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }

    [Fact]
    public async Task HandleChange_WithMissingKeyField_LogsWarning()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "nonexistent"
        };

        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            AddedResults = new[]
            {
                new Dictionary<string, object>
                {
                    { "id", "123" },
                    { "name", "Test Item" }
                }
            }
        };

        // Setup initialized query
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Could not extract key field")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(It.IsAny<McpResource>()), Times.Never);
        
        // Should still update sync point even if some items failed
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }

    [Fact]
    public async Task HandleChange_ProcessesMultipleChanges()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "id"
        };

        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            AddedResults = new[]
            {
                new Dictionary<string, object> { { "id", "1" } },
                new Dictionary<string, object> { { "id", "2" } }
            },
            UpdatedResults = new[]
            {
                new UpdatedResultElement 
                { 
                    Before = new Dictionary<string, object> { { "id", "3" } },
                    After = new Dictionary<string, object> { { "id", "3" }, { "updated", true } }
                }
            },
            DeletedResults = new[]
            {
                new Dictionary<string, object> { { "id", "4" } }
            }
        };

        // Setup initialized query
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(It.IsAny<McpResource>()), Times.Exactly(3));
        _mockResourceStore.Verify(x => x.DeleteEntryAsync("test-query", "4"), Times.Once);
        
        // Verify sync point was updated
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }

    [Fact]
    public async Task HandleChange_HandlesJsonElements()
    {
        // Arrange
        var queryConfig = new McpQueryConfig
        {
            KeyField = "id"
        };

        // Since AddedResults expects Dictionary<string,object>[], we need to convert JsonElement
        var jsonString = "{\"id\": \"123\", \"name\": \"Test Item\"}";
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString)!;
        
        var changeEvent = new ChangeEvent
        {
            QueryId = "test-query",
            Sequence = 10,
            AddedResults = new[] { dict }
        };

        // Setup initialized query
        SetupInitializedQuery("test-query", 5);

        // Act
        await _handler.HandleChange(changeEvent, queryConfig);

        // Assert
        _mockResourceStore.Verify(x => x.UpsertResourceAsync(
            It.Is<McpResource>(r => 
                r.Uri == "drasi://test-reaction/entries/test-query/123" &&
                r.QueryId == "test-query" &&
                r.EntryKey == "123"
            )), Times.Once);
        
        // Verify sync point was updated
        _mockSyncPointManager.Verify(x => x.UpdateSyncPointAsync("test-query", 10), Times.Once);
    }
}