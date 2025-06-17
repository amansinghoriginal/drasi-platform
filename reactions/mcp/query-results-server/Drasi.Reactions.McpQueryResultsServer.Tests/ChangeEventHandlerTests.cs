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
using Drasi.Reaction.SDK.Models.QueryOutput;
using Drasi.Reactions.McpQueryResultsServer.Handlers;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reactions.McpQueryResultsServer.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests;

public class ChangeEventHandlerTests
{
    private readonly Mock<IQueryResultStore> _mockResultStore;
    private readonly Mock<IMcpNotificationService> _mockNotificationService;
    private readonly Mock<ILogger<ChangeEventHandler>> _mockLogger;
    private readonly ChangeEventHandler _handler;

    public ChangeEventHandlerTests()
    {
        _mockResultStore = new Mock<IQueryResultStore>();
        _mockNotificationService = new Mock<IMcpNotificationService>();
        _mockLogger = new Mock<ILogger<ChangeEventHandler>>();
        _handler = new ChangeEventHandler(_mockResultStore.Object, _mockNotificationService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task HandleChange_WithNullConfig_LogsWarningAndReturns()
    {
        // Arrange
        var changeEvent = new ChangeEvent { QueryId = "test-query" };

        // Act
        await _handler.HandleChange(changeEvent, null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("No configuration for query")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _mockResultStore.Verify(x => x.ApplyChangeEventAsync(It.IsAny<ChangeEvent>(), It.IsAny<QueryConfig>()), Times.Never);
    }

    [Fact]
    public async Task HandleChange_WithAddedResults_SendsQueryLevelNotification()
    {
        // Arrange
        var queryId = "test-query";
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } }
            },
            UpdatedResults = Array.Empty<UpdatedResultElement>(),
            DeletedResults = Array.Empty<Dictionary<string, object>>()
        };
        var config = new QueryConfig { KeyField = "id" };
        var expectedUri = $"drasi://queries/{queryId}";

        // Act
        await _handler.HandleChange(changeEvent, config);

        // Assert
        _mockResultStore.Verify(x => x.ApplyChangeEventAsync(changeEvent, config), Times.Once);
        _mockNotificationService.Verify(x => x.SendResourceUpdatedNotificationAsync(expectedUri, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleChange_WithDeletedResults_SendsQueryLevelNotification()
    {
        // Arrange
        var queryId = "test-query";
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = Array.Empty<Dictionary<string, object>>(),
            UpdatedResults = Array.Empty<UpdatedResultElement>(),
            DeletedResults = new[]
            {
                new Dictionary<string, object?> { { "id", "1" }, { "name", "Product 1" } }
            }
        };
        var config = new QueryConfig { KeyField = "id" };
        var expectedUri = $"drasi://queries/{queryId}";

        // Act
        await _handler.HandleChange(changeEvent, config);

        // Assert
        _mockResultStore.Verify(x => x.ApplyChangeEventAsync(changeEvent, config), Times.Once);
        _mockNotificationService.Verify(x => x.SendResourceUpdatedNotificationAsync(expectedUri, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleChange_WithUpdatedResults_SendsEntryLevelNotifications()
    {
        // Arrange
        var queryId = "test-query";
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = Array.Empty<Dictionary<string, object>>(),
            UpdatedResults = new[]
            {
                new UpdatedResultElement
                {
                    After = new Dictionary<string, object?> { { "id", "1" }, { "name", "Updated Product" } }
                }
            },
            DeletedResults = Array.Empty<Dictionary<string, object>>()
        };
        var config = new QueryConfig { KeyField = "id" };
        var expectedUri = $"drasi://entries/{queryId}/1";

        // Act
        await _handler.HandleChange(changeEvent, config);

        // Assert
        _mockResultStore.Verify(x => x.ApplyChangeEventAsync(changeEvent, config), Times.Once);
        _mockNotificationService.Verify(x => x.SendResourceUpdatedNotificationAsync(expectedUri, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleChange_WithMissingKeyField_SkipsEntryNotification()
    {
        // Arrange
        var queryId = "test-query";
        var changeEvent = new ChangeEvent
        {
            QueryId = queryId,
            AddedResults = Array.Empty<Dictionary<string, object>>(),
            UpdatedResults = new[]
            {
                new UpdatedResultElement
                {
                    After = new Dictionary<string, object?> { { "name", "Product without ID" } } // Missing 'id' field
                }
            },
            DeletedResults = Array.Empty<Dictionary<string, object>>()
        };
        var config = new QueryConfig { KeyField = "id" };

        // Act
        await _handler.HandleChange(changeEvent, config);

        // Assert
        _mockResultStore.Verify(x => x.ApplyChangeEventAsync(changeEvent, config), Times.Once);
        // No notifications should be sent for entry without key
        _mockNotificationService.Verify(x => x.SendResourceUpdatedNotificationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}