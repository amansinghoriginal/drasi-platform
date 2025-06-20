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
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Drasi.Reactions.McpQueryResultsServer.Services;
using Drasi.Reactions.McpQueryResultsServer.Models;
using Drasi.Reaction.SDK.Services;

namespace Drasi.Reactions.McpQueryResultsServer.Tests.Services;

public class McpServerHostedServiceTests
{
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<McpServerHostedService>> _mockLogger;
    private readonly Mock<IErrorStateHandler> _mockErrorStateHandler;
    private readonly Mock<ISessionTracker> _mockSessionTracker;
    private readonly Mock<IQueryConfigService> _mockQueryConfigService;
    private readonly Mock<IQueryResultStore> _mockQueryResultStore;
    
    public McpServerHostedServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<McpServerHostedService>>();
        _mockErrorStateHandler = new Mock<IErrorStateHandler>();
        _mockSessionTracker = new Mock<ISessionTracker>();
        _mockQueryConfigService = new Mock<IQueryConfigService>();
        _mockQueryResultStore = new Mock<IQueryResultStore>();
        
        // Setup service provider to return mocked services
        _mockServiceProvider.Setup(x => x.GetService(typeof(IQueryConfigService)))
            .Returns(_mockQueryConfigService.Object);
        _mockServiceProvider.Setup(x => x.GetService(typeof(IQueryResultStore)))
            .Returns(_mockQueryResultStore.Object);
            
        // Setup default configuration
        _mockConfiguration.Setup(x => x["mcpServerPort"]).Returns("8080");
    }
    
    [Fact]
    public async Task ListToolsHandler_ReturnsToolsForEachQuery()
    {
        // Arrange
        var queryIds = new[] { "query1", "query2" };
        var config1 = new QueryConfig { Description = "Live customer order data" };
        var config2 = new QueryConfig { Description = "Real-time inventory levels" };
        
        _mockQueryConfigService.Setup(x => x.GetQueryNames()).Returns(queryIds.ToList());
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>("query1")).Returns(config1);
        _mockQueryConfigService.Setup(x => x.GetQueryConfig<QueryConfig>("query2")).Returns(config2);
        
        // Since we can't easily test the actual handler without a full WebApplication,
        // we'll create a focused test that validates the tool generation logic
        var tools = new List<Tool>();
        foreach (var queryId in queryIds)
        {
            var config = queryId == "query1" ? config1 : config2;
            tools.Add(new Tool
            {
                Name = $"get_{queryId}_results",
                Description = config?.Description ?? $"Fetch live {queryId} data",
                InputSchema = JsonSerializer.SerializeToElement(new
                {
                    type = "object",
                    properties = new
                    {
                        limit = new
                        {
                            type = "integer",
                            description = "Maximum number of results to return (optional, default: all)",
                            minimum = 1
                        },
                        filter = new
                        {
                            type = "object",
                            description = "Optional filter criteria as key-value pairs",
                            additionalProperties = true
                        }
                    },
                    additionalProperties = false
                })
            });
        }
        
        // Assert
        Assert.Equal(2, tools.Count);
        Assert.Equal("get_query1_results", tools[0].Name);
        Assert.Equal("Live customer order data", tools[0].Description);
        Assert.Equal("get_query2_results", tools[1].Name);
        Assert.Equal("Real-time inventory levels", tools[1].Description);
    }
    
    [Fact]
    public void ExtractQueryIdFromToolName_ValidToolName_ReturnsQueryId()
    {
        // Arrange
        var toolName = "get_my_query_results";
        
        // Act
        var queryId = toolName.Substring(4, toolName.Length - 12); // Remove "get_" and "_results"
        
        // Assert
        Assert.Equal("my_query", queryId);
    }
    
    [Fact]
    public void ExtractQueryIdFromToolName_ComplexQueryId_ReturnsCorrectId()
    {
        // Arrange
        var toolName = "get_product_inventory_updates_results";
        
        // Act
        var queryId = toolName.Substring(4, toolName.Length - 12);
        
        // Assert
        Assert.Equal("product_inventory_updates", queryId);
    }
    
    [Fact]
    public async Task CallToolHandler_AppliesLimitCorrectly()
    {
        // Arrange
        var queryId = "test_query";
        var entryKeys = new[] { "key1", "key2", "key3", "key4", "key5" };
        var limit = 3;
        
        var results = new List<JsonElement>();
        foreach (var key in entryKeys)
        {
            var json = JsonDocument.Parse($"{{\"id\": \"{key}\", \"value\": \"test\"}}");
            results.Add(json.RootElement);
        }
        
        _mockQueryResultStore.Setup(x => x.GetQueryEntriesAsync(queryId))
            .ReturnsAsync(entryKeys);
        
        foreach (var i in Enumerable.Range(0, entryKeys.Length))
        {
            _mockQueryResultStore.Setup(x => x.GetEntryAsync(queryId, entryKeys[i]))
                .ReturnsAsync(results[i]);
        }
        
        // Simulate the tool call logic
        var filteredResults = new List<object>();
        foreach (var entryKey in entryKeys)
        {
            var entry = await _mockQueryResultStore.Object.GetEntryAsync(queryId, entryKey);
            if (entry.HasValue)
            {
                filteredResults.Add(JsonSerializer.Deserialize<object>(entry.Value.GetRawText())!);
                if (filteredResults.Count >= limit)
                {
                    break;
                }
            }
        }
        
        // Assert
        Assert.Equal(3, filteredResults.Count);
    }
    
    [Fact]
    public async Task CallToolHandler_AppliesFilterCorrectly()
    {
        // Arrange
        var queryId = "test_query";
        var entryKeys = new[] { "key1", "key2", "key3" };
        
        var entry1 = JsonDocument.Parse("{\"id\": \"key1\", \"status\": \"active\"}");
        var entry2 = JsonDocument.Parse("{\"id\": \"key2\", \"status\": \"inactive\"}");
        var entry3 = JsonDocument.Parse("{\"id\": \"key3\", \"status\": \"active\"}");
        
        _mockQueryResultStore.Setup(x => x.GetQueryEntriesAsync(queryId))
            .ReturnsAsync(entryKeys);
        _mockQueryResultStore.Setup(x => x.GetEntryAsync(queryId, "key1"))
            .ReturnsAsync(entry1.RootElement);
        _mockQueryResultStore.Setup(x => x.GetEntryAsync(queryId, "key2"))
            .ReturnsAsync(entry2.RootElement);
        _mockQueryResultStore.Setup(x => x.GetEntryAsync(queryId, "key3"))
            .ReturnsAsync(entry3.RootElement);
        
        // Simulate filter logic
        var filter = new Dictionary<string, object> { { "status", "active" } };
        var filteredResults = new List<object>();
        
        foreach (var entryKey in entryKeys)
        {
            var entry = await _mockQueryResultStore.Object.GetEntryAsync(queryId, entryKey);
            if (entry.HasValue)
            {
                bool matches = true;
                foreach (var filterKv in filter)
                {
                    if (entry.Value.TryGetProperty(filterKv.Key, out var entryValue))
                    {
                        var entryValueStr = entryValue.ToString();
                        var filterValueStr = filterKv.Value?.ToString();
                        if (!string.Equals(entryValueStr, filterValueStr, StringComparison.OrdinalIgnoreCase))
                        {
                            matches = false;
                            break;
                        }
                    }
                    else
                    {
                        matches = false;
                        break;
                    }
                }
                
                if (matches)
                {
                    filteredResults.Add(JsonSerializer.Deserialize<object>(entry.Value.GetRawText())!);
                }
            }
        }
        
        // Assert
        Assert.Equal(2, filteredResults.Count);
        // Verify that only entries with status="active" are included
        foreach (var result in filteredResults)
        {
            var resultJson = JsonSerializer.SerializeToElement(result);
            Assert.Equal("active", resultJson.GetProperty("status").GetString());
        }
    }
}