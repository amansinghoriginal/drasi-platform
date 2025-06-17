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
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using System.Text.Json;
using Drasi.Reactions.McpQueryResultsServer.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests.Services;

public class McpNotificationServiceTests
{
    private readonly Mock<ISessionTracker> _mockSessionTracker;
    private readonly Mock<ILogger<McpNotificationService>> _mockLogger;
    private readonly McpNotificationService _service;

    public McpNotificationServiceTests()
    {
        _mockSessionTracker = new Mock<ISessionTracker>();
        _mockLogger = new Mock<ILogger<McpNotificationService>>();
        _service = new McpNotificationService(_mockLogger.Object, _mockSessionTracker.Object);
    }

    [Fact]
    public async Task SendResourceUpdatedNotificationAsync_WithNoSessions_LogsDebugMessage()
    {
        // Arrange
        var uri = "drasi://queries/test-query";
        var emptySessions = new List<IMcpServer>();
        
        _mockSessionTracker.Setup(x => x.GetActiveSessions()).Returns(emptySessions);

        // Act
        await _service.SendResourceUpdatedNotificationAsync(uri, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("No active MCP sessions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendResourceUpdatedNotificationAsync_WithActiveSessions_CallsSendNotification()
    {
        // Arrange
        var uri = "drasi://queries/test-query";
        var mockServer1 = new Mock<IMcpServer>();
        var mockServer2 = new Mock<IMcpServer>();
        var sessions = new List<IMcpServer> { mockServer1.Object, mockServer2.Object };

        _mockSessionTracker.Setup(x => x.GetActiveSessions()).Returns(sessions);

        // Act
        await _service.SendResourceUpdatedNotificationAsync(uri, CancellationToken.None);

        // Assert
        // Since we can't directly verify the extension method call, 
        // we verify that the debug log was written indicating success
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => 
                    o.ToString()!.Contains("Sent resource update notification") && 
                    o.ToString()!.Contains(uri) &&
                    o.ToString()!.Contains("2 sessions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task SendResourceUpdatedNotificationAsync_LogsNotificationDetails()
    {
        // Arrange
        var uri = "drasi://entries/test-query/key1";
        var mockServer = new Mock<IMcpServer>();
        var sessions = new List<IMcpServer> { mockServer.Object };

        _mockSessionTracker.Setup(x => x.GetActiveSessions()).Returns(sessions);

        // Act
        await _service.SendResourceUpdatedNotificationAsync(uri, CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => 
                    o.ToString()!.Contains("Sent resource update notification") && 
                    o.ToString()!.Contains(uri) &&
                    o.ToString()!.Contains("1 sessions")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}