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

namespace Drasi.Reactions.McpQueryResultsServer.Tests;

public class ControlEventHandlerTests
{
    private readonly Mock<ILogger<ControlEventHandler>> _mockLogger;
    private readonly ControlEventHandler _handler;

    public ControlEventHandlerTests()
    {
        _mockLogger = new Mock<ILogger<ControlEventHandler>>();
        _handler = new ControlEventHandler(_mockLogger.Object);
    }

    [Fact]
    public async Task HandleControlSignal_LogsControlEvent()
    {
        // Arrange
        var queryId = "test-query";
        var controlEvent = new ControlEvent
        {
            QueryId = queryId,
            Kind = ControlEventKind.Control
        };
        var config = new QueryConfig();

        // Act
        await _handler.HandleControlSignal(controlEvent, config);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => 
                    o.ToString()!.Contains("Received control event") && 
                    o.ToString()!.Contains(queryId)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleControlSignal_WithNullConfig_StillLogs()
    {
        // Arrange
        var queryId = "test-query";
        var controlEvent = new ControlEvent
        {
            QueryId = queryId,
            Kind = ControlEventKind.Control
        };

        // Act
        await _handler.HandleControlSignal(controlEvent, null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Received control event")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleControlSignal_ReturnsCompletedTask()
    {
        // Arrange
        var controlEvent = new ControlEvent
        {
            QueryId = "test-query",
            Kind = ControlEventKind.Control
        };

        // Act
        var task = _handler.HandleControlSignal(controlEvent, null);
        await task;

        // Assert
        Assert.True(task.IsCompletedSuccessfully);
    }
}