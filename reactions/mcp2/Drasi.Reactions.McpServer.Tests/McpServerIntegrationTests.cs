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

using System.Text.Json;
using Drasi.Reactions.McpServer.Models;
using Drasi.Reactions.McpServer.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Drasi.Reactions.McpServer.Tests.Integration;

/// <summary>
/// Integration tests that demonstrate the MCP server working with multiple services
/// </summary>
public class McpServerIntegrationTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IResourceStoreService _resourceStore;

    public McpServerIntegrationTests()
    {
        var services = new ServiceCollection();
        
        // Add configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["REACTION_NAME"] = "test-mcp-server"
            })
            .Build();
        
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IResourceStoreService, ResourceStoreService>();
        
        _serviceProvider = services.BuildServiceProvider();
        _resourceStore = _serviceProvider.GetRequiredService<IResourceStoreService>();
    }

    [Fact]
    public void Should_Handle_Multiple_Queries_And_Entries()
    {
        // Arrange
        var customerConfig = new QueryConfig
        {
            KeyField = "customerId",
            ResourceContentType = "application/json",
            Description = "Customer data from CRM"
        };
        
        var orderConfig = new QueryConfig
        {
            KeyField = "orderId",
            ResourceContentType = "application/json",
            Description = "Order tracking data"
        };

        // Act - Add customer entries
        _resourceStore.UpdateEntry("customers", "cust-001", 
            JsonDocument.Parse(@"{""customerId"": ""cust-001"", ""name"": ""Alice Smith"", ""status"": ""active""}").RootElement, 
            customerConfig);
        
        _resourceStore.UpdateEntry("customers", "cust-002", 
            JsonDocument.Parse(@"{""customerId"": ""cust-002"", ""name"": ""Bob Jones"", ""status"": ""inactive""}").RootElement, 
            customerConfig);

        // Act - Add order entries
        _resourceStore.UpdateEntry("orders", "order-123", 
            JsonDocument.Parse(@"{""orderId"": ""order-123"", ""customerId"": ""cust-001"", ""total"": 99.99}").RootElement, 
            orderConfig);

        // Assert - Check all resources exist
        var allResources = _resourceStore.ListAllResources();
        allResources.Should().HaveCount(5); // 2 queries + 3 entries

        // Assert - Check query-specific entries
        var customerEntries = _resourceStore.GetQueryEntries("customers");
        customerEntries.Should().HaveCount(2);
        
        var orderEntries = _resourceStore.GetQueryEntries("orders");
        orderEntries.Should().HaveCount(1);

        // Assert - Check individual resources
        var customer = _resourceStore.GetResource("drasi://test-mcp-server/queries/customers/entries/cust-001");
        customer.Should().NotBeNull();
        customer!.Content.Should().NotBeNull();
    }

    [Fact]
    public void Should_Handle_Entry_Updates_And_Deletions()
    {
        // Arrange
        var config = new QueryConfig { KeyField = "id" };
        var initialData = JsonDocument.Parse(@"{""id"": ""item-1"", ""value"": ""initial""}").RootElement;
        var updatedData = JsonDocument.Parse(@"{""id"": ""item-1"", ""value"": ""updated""}").RootElement;

        // Act - Create initial entry
        _resourceStore.UpdateEntry("test-query", "item-1", initialData, config);
        var initialResource = _resourceStore.GetResource("drasi://test-mcp-server/queries/test-query/entries/item-1");

        // Act - Update entry
        _resourceStore.UpdateEntry("test-query", "item-1", updatedData, config);
        var updatedResource = _resourceStore.GetResource("drasi://test-mcp-server/queries/test-query/entries/item-1");

        // Act - Remove entry
        _resourceStore.RemoveEntry("test-query", "item-1");
        var deletedResource = _resourceStore.GetResource("drasi://test-mcp-server/queries/test-query/entries/item-1");

        // Assert
        initialResource.Should().NotBeNull();
        updatedResource.Should().NotBeNull();
        updatedResource!.Content?.ToString().Should().Contain("updated");
        deletedResource.Should().BeNull();
    }

    [Fact]
    public void Should_Support_Subscriptions()
    {
        // Arrange
        var config = new QueryConfig { KeyField = "id" };
        var resourceChangedEvents = new List<ResourceChangedEventArgs>();
        var listChangedEvents = new List<ResourceListChangedEventArgs>();
        
        _resourceStore.ResourceChanged += (sender, args) => resourceChangedEvents.Add(args);
        _resourceStore.ResourceListChanged += (sender, args) => listChangedEvents.Add(args);

        // Act - Subscribe clients
        _resourceStore.Subscribe("drasi://test-mcp-server/queries/monitoring", "client-1");
        _resourceStore.Subscribe("drasi://test-mcp-server/queries/monitoring", "client-2");
        _resourceStore.Subscribe("drasi://test-mcp-server/queries/monitoring/entries/server-1", "client-1");

        // Act - Add entry (should trigger events)
        _resourceStore.UpdateEntry("monitoring", "server-1", 
            JsonDocument.Parse(@"{""id"": ""server-1"", ""status"": ""healthy""}").RootElement, 
            config);

        // Assert - Check subscriptions
        var querySubscribers = _resourceStore.GetSubscribers("drasi://test-mcp-server/queries/monitoring");
        querySubscribers.Should().HaveCount(2);
        querySubscribers.Should().Contain("client-1");
        querySubscribers.Should().Contain("client-2");

        // Assert - Check events were raised
        resourceChangedEvents.Should().NotBeEmpty();
        listChangedEvents.Should().NotBeEmpty();
        
        // Should have events for both query creation and entry creation
        resourceChangedEvents.Should().Contain(e => e.Uri.Contains("queries/monitoring"));
        resourceChangedEvents.Should().Contain(e => e.Uri.Contains("queries/monitoring/entries/server-1"));
    }
}