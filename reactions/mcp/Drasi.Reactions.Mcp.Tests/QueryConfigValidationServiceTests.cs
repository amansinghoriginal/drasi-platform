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

using Drasi.Reaction.SDK.Services;
using Drasi.Reactions.Mcp.Models;
using Drasi.Reactions.Mcp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Drasi.Reactions.Mcp.Tests;

public class QueryConfigValidationServiceTests
{
    private readonly Mock<ILogger<QueryConfigValidationService>> _mockLogger;
    private readonly Mock<IQueryConfigService> _mockQueryConfigService;
    private readonly QueryConfigValidationService _validationService;

    public QueryConfigValidationServiceTests()
    {
        _mockLogger = new Mock<ILogger<QueryConfigValidationService>>();
        _mockQueryConfigService = new Mock<IQueryConfigService>();
        _validationService = new QueryConfigValidationService(_mockLogger.Object, _mockQueryConfigService.Object);
    }

    [Fact]
    public async Task ValidateAllAsync_WithValidConfigs_ReturnsTrue()
    {
        // Arrange
        var queryNames = new List<string> { "query1", "query2" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Returns(new McpQueryConfig 
            { 
                KeyField = "id",
                ResourceContentType = "application/json",
                Description = "Test Query 1"
            });
            
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query2"))
            .Returns(new McpQueryConfig 
            { 
                KeyField = "key",
                ResourceContentType = "application/xml"
            });

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeTrue();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("validated successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateAllAsync_WithNullConfig_ReturnsFalse()
    {
        // Arrange
        var queryNames = new List<string> { "query1" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Returns((McpQueryConfig?)null);

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeFalse();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("null configuration")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAllAsync_WithMissingKeyField_ReturnsFalse()
    {
        // Arrange
        var queryNames = new List<string> { "query1" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Returns(new McpQueryConfig { KeyField = null! });

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeFalse();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("keyField is required")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAllAsync_WithEmptyKeyField_ReturnsFalse()
    {
        // Arrange
        var queryNames = new List<string> { "query1" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Returns(new McpQueryConfig { KeyField = "" });

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeFalse();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("empty keyField")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAllAsync_WithException_ReturnsFalse()
    {
        // Arrange
        var queryNames = new List<string> { "query1" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Throws(new InvalidOperationException("Config error"));

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeFalse();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Failed to validate")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidateAllAsync_WithMixedValidAndInvalid_ReturnsFalse()
    {
        // Arrange
        var queryNames = new List<string> { "query1", "query2", "query3" };
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryNames);
        
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query1"))
            .Returns(new McpQueryConfig { KeyField = "id" });
            
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query2"))
            .Returns(new McpQueryConfig { KeyField = "" }); // Invalid
            
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<McpQueryConfig>("query3"))
            .Returns(new McpQueryConfig { KeyField = "key" });

        // Act
        var result = await _validationService.ValidateAllAsync();

        // Assert
        result.Should().BeFalse();
    }
}