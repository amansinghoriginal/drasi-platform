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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Drasi.Reactions.McpServer.Tests.Services;

public class ResourceStoreServiceTests
{
    private readonly ResourceStoreService _sut;
    private readonly Mock<ILogger<ResourceStoreService>> _loggerMock;
    private readonly Mock<IConfiguration> _configMock;

    public ResourceStoreServiceTests()
    {
        _loggerMock = new Mock<ILogger<ResourceStoreService>>();
        _configMock = new Mock<IConfiguration>();
        _configMock.SetupGet(x => x["REACTION_NAME"]).Returns("test-reaction");
        _sut = new ResourceStoreService(_configMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void UpdateEntry_Should_Create_New_Entry()
    {
        // Arrange
        var queryId = "test-query";
        var key = "entry-1";
        var data = JsonDocument.Parse(@"{""id"": ""entry-1"", ""value"": 42}").RootElement;
        var config = new QueryConfig
        {
            KeyField = "id",
            ResourceContentType = "application/json",
            Description = "Test Query"
        };

        // Act
        _sut.UpdateEntry(queryId, key, data, config);

        // Assert
        var entryUri = $"drasi://test-reaction/queries/{queryId}/entries/{key}";
        var resource = _sut.GetResource(entryUri);
        resource.Should().NotBeNull();
        resource!.Name.Should().Be($"{queryId}/{key}");
        resource.Content.Should().NotBeNull();
    }

    [Fact]
    public void UpdateEntry_Should_Create_Query_Resource_If_Not_Exists()
    {
        // Arrange
        var queryId = "new-query";
        var key = "entry-1";
        var data = JsonDocument.Parse(@"{""id"": ""entry-1""}").RootElement;
        var config = new QueryConfig
        {
            KeyField = "id",
            Description = "New Query Description"
        };

        // Act
        _sut.UpdateEntry(queryId, key, data, config);

        // Assert
        var queryUri = $"drasi://test-reaction/queries/{queryId}";
        var queryResource = _sut.GetResource(queryUri);
        queryResource.Should().NotBeNull();
        queryResource!.Description.Should().Be("New Query Description");
    }

    [Fact]
    public void RemoveEntry_Should_Remove_Existing_Entry()
    {
        // Arrange
        var queryId = "test-query";
        var key = "entry-1";
        var data = JsonDocument.Parse(@"{""id"": ""entry-1""}").RootElement;
        var config = new QueryConfig { KeyField = "id" };
        
        _sut.UpdateEntry(queryId, key, data, config);

        // Act
        _sut.RemoveEntry(queryId, key);

        // Assert
        var entryUri = $"drasi://test-reaction/queries/{queryId}/entries/{key}";
        var resource = _sut.GetResource(entryUri);
        resource.Should().BeNull();
    }

    [Fact]
    public void ListAllResources_Should_Return_All_Resources()
    {
        // Arrange
        var config = new QueryConfig { KeyField = "id" };
        _sut.UpdateEntry("query1", "entry1", JsonDocument.Parse(@"{}").RootElement, config);
        _sut.UpdateEntry("query1", "entry2", JsonDocument.Parse(@"{}").RootElement, config);
        _sut.UpdateEntry("query2", "entry1", JsonDocument.Parse(@"{}").RootElement, config);

        // Act
        var resources = _sut.ListAllResources();

        // Assert
        resources.Should().HaveCount(5); // 2 queries + 3 entries
        resources.Should().Contain(r => r.Uri == "drasi://test-reaction/queries/query1");
        resources.Should().Contain(r => r.Uri == "drasi://test-reaction/queries/query2");
    }

    [Fact]
    public void GetQueryEntries_Should_Return_Only_Query_Entries()
    {
        // Arrange
        var config = new QueryConfig { KeyField = "id" };
        _sut.UpdateEntry("query1", "entry1", JsonDocument.Parse(@"{}").RootElement, config);
        _sut.UpdateEntry("query1", "entry2", JsonDocument.Parse(@"{}").RootElement, config);
        _sut.UpdateEntry("query2", "entry1", JsonDocument.Parse(@"{}").RootElement, config);

        // Act
        var entries = _sut.GetQueryEntries("query1");

        // Assert
        entries.Should().HaveCount(2);
        entries.Should().AllSatisfy(e => e.Uri.Should().Contain("query1"));
    }

    [Fact]
    public void Subscribe_Should_Add_Client_To_Subscription()
    {
        // Arrange
        var uri = "drasi://test-reaction/queries/query1/entries/entry1";
        var clientId = "client-123";

        // Act
        _sut.Subscribe(uri, clientId);

        // Assert
        var subscribers = _sut.GetSubscribers(uri);
        subscribers.Should().Contain(clientId);
    }

    [Fact]
    public void UpdateEntry_Should_Raise_ResourceChanged_Event()
    {
        // Arrange
        var queryId = "test-query";
        var key = "entry-1";
        var data = JsonDocument.Parse(@"{""id"": ""entry-1""}").RootElement;
        var config = new QueryConfig { KeyField = "id" };
        
        var capturedEvents = new List<ResourceChangedEventArgs>();
        _sut.ResourceChanged += (sender, args) => capturedEvents.Add(args);

        // Act
        _sut.UpdateEntry(queryId, key, data, config);

        // Assert
        capturedEvents.Should().NotBeEmpty();
        capturedEvents.Should().Contain(e => e.Uri.Contains($"queries/{queryId}/entries/{key}") && e.Type == ChangeType.Created);
    }
}