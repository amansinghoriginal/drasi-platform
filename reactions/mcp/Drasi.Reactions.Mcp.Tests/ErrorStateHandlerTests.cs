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

using Drasi.Reactions.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Drasi.Reactions.Mcp.Tests;

public class ErrorStateHandlerTests
{
    private readonly Mock<IHostApplicationLifetime> _mockLifetime;
    private readonly Mock<ILogger<ErrorStateHandler>> _mockLogger;
    private readonly ErrorStateHandler _handler;

    public ErrorStateHandlerTests()
    {
        _mockLifetime = new Mock<IHostApplicationLifetime>();
        _mockLogger = new Mock<ILogger<ErrorStateHandler>>();
        _handler = new ErrorStateHandler(_mockLifetime.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task HandleFatalErrorAsync_LogsCriticalError()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var message = "Custom error message";

        // Act
        await _handler.HandleFatalErrorAsync(exception, message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Fatal error") && o.ToString()!.Contains(message)),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleFatalErrorAsync_SetsExitCode()
    {
        // Arrange
        var exception = new Exception("Test");
        var originalExitCode = Environment.ExitCode;

        try
        {
            // Act
            await _handler.HandleFatalErrorAsync(exception);

            // Assert
            Environment.ExitCode.Should().Be(1);
        }
        finally
        {
            // Cleanup
            Environment.ExitCode = originalExitCode;
        }
    }

    [Fact]
    public async Task HandleFatalErrorAsync_TriggersApplicationShutdown()
    {
        // Arrange
        var exception = new Exception("Test");

        // Act
        await _handler.HandleFatalErrorAsync(exception);

        // Assert
        _mockLifetime.Verify(x => x.StopApplication(), Times.Once);
    }

    [Fact]
    public async Task HandleFatalErrorAsync_UsesExceptionMessageWhenCustomMessageNull()
    {
        // Arrange
        var exception = new InvalidOperationException("Exception message");

        // Act
        await _handler.HandleFatalErrorAsync(exception, null);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Critical,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Exception message")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}