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
using Drasi.Reactions.McpQueryResultsServer.Services;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reaction.SDK.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests.Services;

public class QueryConfigValidationServiceTests
{
    private readonly Mock<ILogger<QueryConfigValidationService>> _mockLogger;
    private readonly Mock<IQueryConfigService> _mockQueryConfigService;
    private readonly Mock<IErrorStateHandler> _mockErrorStateHandler;
    private readonly QueryConfigValidationService _service;

    public QueryConfigValidationServiceTests()
    {
        _mockLogger = new Mock<ILogger<QueryConfigValidationService>>();
        _mockQueryConfigService = new Mock<IQueryConfigService>();
        _mockErrorStateHandler = new Mock<IErrorStateHandler>();
        _service = new QueryConfigValidationService(_mockLogger.Object, _mockQueryConfigService.Object, _mockErrorStateHandler.Object);
    }

    [Fact]
    public async Task ValidateQueryConfigsAsync_WithNoQueries_LogsWarning()
    {
        // Arrange
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(new List<string>());

        // Act
        await _service.ValidateQueryConfigsAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("No queries configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _mockErrorStateHandler.Verify(x => x.Terminate(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ValidateQueryConfigsAsync_WithValidConfigs_SuccessfullyValidates()
    {
        // Arrange
        var queryNames = new[] { "query1", "query2" };
        var config1 = new QueryConfig
        {
            KeyField = "id",
            ResourceContentType = "application/json",
            Description = "Query 1"
        };
        var config2 = new QueryConfig
        {
            KeyField = "key",
            ResourceContentType = "text/plain",
            Description = "Query 2"
        };

        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames.ToList());
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>("query1")).Returns(config1);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>("query2")).Returns(config2);

        // Act
        await _service.ValidateQueryConfigsAsync(CancellationToken.None);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("All query configurations validated successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _mockErrorStateHandler.Verify(x => x.Terminate(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ValidateQueryConfigsAsync_WithNullConfig_TerminatesAndThrows()
    {
        // Arrange
        var queryName = "null-query";
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(new List<string> { queryName });
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryName)).Returns((QueryConfig?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ValidateQueryConfigsAsync(CancellationToken.None));

        Assert.Contains($"Query configuration for '{queryName}' is null", exception.Message);
        _mockErrorStateHandler.Verify(x => x.Terminate(It.Is<string>(s => s.Contains("is null"))), Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateQueryConfigsAsync_WithInvalidConfig_TerminatesAndThrows()
    {
        // Arrange
        var queryName = "invalid-query";
        var invalidConfig = new QueryConfig
        {
            KeyField = null!, // Required field is null
            ResourceContentType = "application/json"
        };

        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(new List<string> { queryName });
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryName)).Returns(invalidConfig);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ValidateQueryConfigsAsync(CancellationToken.None));

        Assert.Contains("Configuration validation failed", exception.Message);
        _mockErrorStateHandler.Verify(x => x.Terminate(It.Is<string>(s => s.Contains("validation failed"))), Times.Once);
    }

    [Fact]
    public async Task ValidateQueryConfigsAsync_WhenConfigServiceThrows_TerminatesAndRethrows()
    {
        // Arrange
        var queryName = "error-query";
        var configException = new InvalidOperationException("Config service error");

        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(new List<string> { queryName });
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>(queryName)).Throws(configException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ValidateQueryConfigsAsync(CancellationToken.None));

        Assert.Same(configException, exception);
        _mockErrorStateHandler.Verify(x => x.Terminate(It.Is<string>(s => 
            s.Contains("Failed to retrieve query configuration") && 
            s.Contains(queryName) && 
            s.Contains("Config service error"))), Times.Once);
    }
}