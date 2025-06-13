# Drasi MCP Server Reaction Tests

This test suite verifies the functionality of the Drasi MCP Server Reaction.

## Test Coverage

### Unit Tests

#### ResourceStoreServiceTests (7 tests)
- ✅ UpdateEntry_Should_Create_New_Entry
- ✅ UpdateEntry_Should_Create_Query_Resource_If_Not_Exists  
- ✅ RemoveEntry_Should_Remove_Existing_Entry
- ✅ ListAllResources_Should_Return_All_Resources
- ✅ GetQueryEntries_Should_Return_Only_Query_Entries
- ✅ Subscribe_Should_Add_Client_To_Subscription
- ✅ UpdateEntry_Should_Raise_ResourceChanged_Event

### Integration Tests

#### McpServerIntegrationTests (3 tests)
- ✅ Should_Handle_Multiple_Queries_And_Entries
- ✅ Should_Handle_Entry_Updates_And_Deletions
- ✅ Should_Support_Subscriptions

## Running Tests

Run all tests:
```bash
dotnet test
```

Run with detailed output:
```bash
dotnet test --verbosity detailed
```

Run with coverage:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Structure

The tests focus on the core functionality of the MCP Server reaction:

1. **Resource Management**: Tests verify that resources (queries and entries) can be created, updated, and removed correctly.

2. **Event System**: Tests ensure that resource changes trigger appropriate events for subscribed clients.

3. **Query Organization**: Tests verify that entries are properly organized under their parent queries.

4. **Subscription Management**: Tests confirm that clients can subscribe to resources and receive updates.

## Notes

- Tests use in-memory implementations for fast execution
- Integration tests demonstrate real-world scenarios with multiple queries and entries
- All tests follow the Arrange-Act-Assert pattern for clarity
- Tests validate the hierarchical URI structure: `drasi://{reactionName}/queries/{queryId}/entries/{entryId}`
- Tests cover both unit-level functionality and integration scenarios