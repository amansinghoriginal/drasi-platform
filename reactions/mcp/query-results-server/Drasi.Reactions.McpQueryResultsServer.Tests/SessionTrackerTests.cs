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
using Drasi.Reactions.McpQueryResultsServer.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests;

public class SessionTrackerTests
{
    private readonly Mock<ILogger<SessionTracker>> _mockLogger;
    private readonly SessionTracker _tracker;

    public SessionTrackerTests()
    {
        _mockLogger = new Mock<ILogger<SessionTracker>>();
        _tracker = new SessionTracker(_mockLogger.Object);
    }

    [Fact]
    public void AddSession_AddsNewSession()
    {
        // Arrange
        var sessionId = "session1";
        var mockServer = new Mock<IMcpServer>().Object;

        // Act
        _tracker.AddSession(sessionId, mockServer);
        var sessions = _tracker.GetActiveSessions().ToList();

        // Assert
        Assert.Single(sessions);
        Assert.Same(mockServer, sessions[0]);
    }

    [Fact]
    public void AddSession_WithDuplicateId_ReplacesExisting()
    {
        // Arrange
        var sessionId = "session1";
        var mockServer1 = new Mock<IMcpServer>().Object;
        var mockServer2 = new Mock<IMcpServer>().Object;

        // Act
        _tracker.AddSession(sessionId, mockServer1);
        _tracker.AddSession(sessionId, mockServer2);
        var sessions = _tracker.GetActiveSessions().ToList();

        // Assert
        Assert.Single(sessions);
        Assert.Same(mockServer2, sessions[0]);
    }

    [Fact]
    public void RemoveSession_RemovesExistingSession()
    {
        // Arrange
        var sessionId = "session1";
        var mockServer = new Mock<IMcpServer>().Object;
        _tracker.AddSession(sessionId, mockServer);

        // Act
        _tracker.RemoveSession(sessionId);
        var sessions = _tracker.GetActiveSessions().ToList();

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public void RemoveSession_WithNonExistentId_DoesNothing()
    {
        // Arrange
        var sessionId1 = "session1";
        var mockServer = new Mock<IMcpServer>().Object;
        _tracker.AddSession(sessionId1, mockServer);

        // Act
        _tracker.RemoveSession("non-existent");
        var sessions = _tracker.GetActiveSessions().ToList();

        // Assert
        Assert.Single(sessions);
    }

    [Fact]
    public void GetActiveSessions_WithNoSessions_ReturnsEmpty()
    {
        // Act
        var sessions = _tracker.GetActiveSessions();

        // Assert
        Assert.NotNull(sessions);
        Assert.Empty(sessions);
    }

    [Fact]
    public void GetActiveSessions_WithMultipleSessions_ReturnsAll()
    {
        // Arrange
        var mockServer1 = new Mock<IMcpServer>().Object;
        var mockServer2 = new Mock<IMcpServer>().Object;
        var mockServer3 = new Mock<IMcpServer>().Object;

        _tracker.AddSession("session1", mockServer1);
        _tracker.AddSession("session2", mockServer2);
        _tracker.AddSession("session3", mockServer3);

        // Act
        var sessions = _tracker.GetActiveSessions().ToList();

        // Assert
        Assert.Equal(3, sessions.Count);
        Assert.Contains(mockServer1, sessions);
        Assert.Contains(mockServer2, sessions);
        Assert.Contains(mockServer3, sessions);
    }
}