# MCP Server Implementation Analysis and Test Results

## Executive Summary

The MCP Server reaction for Drasi has been implemented and deployed successfully, but end-to-end testing reveals several critical issues that prevent it from functioning as expected. While the server runs and responds to basic MCP protocol requests, it fails to properly expose Drasi queries as MCP resources and doesn't process change events from the Drasi system.

## Test Environment Setup

### Infrastructure
- **PostgreSQL Database**: Deployed with replication support, tables for `customers` and `orders`
- **Drasi Source**: PostgreSQL source (`mcp-test-pg-source`) monitoring both tables
- **Drasi Queries**: 
  - `customer-data`: Monitors customer table changes
  - `order-data`: Monitors order table changes
- **MCP Server Reaction**: Deployed as `mcp-server-e2e` with dual endpoints (port 80 for Dapr, port 8080 for MCP)

### Test Configuration
```yaml
# Reaction configuration
queries:
  customer-data: |
    {
      "keyField": "customer_id",
      "resourceContentType": "application/json",
      "description": "E2E test customer data from PostgreSQL"
    }
  order-data: |
    {
      "keyField": "order_id",
      "resourceContentType": "application/json",
      "description": "E2E test order data from PostgreSQL"
    }
```

## What's Working ✅

### 1. Basic Infrastructure
- MCP Server deploys and runs successfully
- Health endpoint responds with "OK" on `/health`
- Server accepts connections on port 8080
- Kubernetes services are created correctly:
  - `mcp-server-e2e-dapr-http` (port 80)
  - `mcp-server-e2e-mcp-server` (port 8080)

### 2. MCP Protocol Basics
- Server responds to MCP initialization requests
- Supports Server-Sent Events (SSE) transport
- Returns proper JSON-RPC 2.0 responses
- Server capabilities include:
  ```json
  {
    "logging": {},
    "resources": {
      "listChanged": true
    }
  }
  ```

### 3. Query Initialization
Server logs confirm it receives and initializes queries:
```
info: Drasi.Reactions.McpServer.Services.QueryInitializationService[0]
      Initializing 2 queries
info: Drasi.Reactions.McpServer.Services.QueryInitializationService[0]
      Initializing query order-data
info: Drasi.Reactions.McpServer.Services.QueryInitializationService[0]
      Initialized query order-data with 0 entries
info: Drasi.Reactions.McpServer.Services.QueryInitializationService[0]
      Initializing query customer-data
info: Drasi.Reactions.McpServer.Services.QueryInitializationService[0]
      Initialized query customer-data with 0 entries
```

## What's Not Working ❌

### 1. Query Resource Exposure Problem

**Expected Behavior**: The server should expose individual query resources that can be listed and accessed via MCP.

**Actual Behavior**: 
- Server returns only ONE resource: a `queries` collection
- When reading this collection, it returns an empty array:
  ```json
  {
    "uri": "queries",
    "name": "Drasi Queries",
    "description": "List of all available Drasi queries",
    "mimeType": "application/json"
  }
  ```
- Reading the `queries` resource returns:
  ```json
  {
    "queries": []
  }
  ```

**Impact**: MCP clients cannot discover available queries or their metadata.

### 2. Change Event Processing Failure

**Expected Behavior**: When data is inserted/updated/deleted in PostgreSQL, the MCP server should:
1. Receive change events via Dapr pubsub
2. Create/update/delete corresponding MCP resources
3. Make these resources available via the MCP API

**Actual Behavior**:
- No change event processing logs appear
- Entry resources are never created
- All resource read attempts fail with "Unknown resource URI" or "Entry not found"
- Server logs show repeated attempts to find non-existent entries:
  ```
  ModelContextProtocol.McpException: Unknown resource URI: 'drasi://mcp-server-e2e/queries/customer-data/entries/cust-1749873520707'
  System.NotSupportedException: Entry not found: cust-1749873520707 in query: customer-data
  ```

**Impact**: The server never reflects any data changes, making it useless for real-time data access.

### 3. Resource URI Structure Issues

**Expected URI Patterns Tested**:
- `drasi://mcp-server-e2e/queries/customer-data/entries/{id}`
- `customer-data/entries/{id}`
- `queries/customer-data/entries/{id}`

**Results**:
- All URI patterns fail
- The `queries/customer-data/entries/{id}` pattern returns generic "An error occurred" 
- Other patterns return "Unknown resource URI"

**Impact**: Even if entries existed, clients might not be able to access them due to URI mismatches.

### 4. Resource Discovery Problems

**Expected Behavior**: 
- `resources/list` should return all available resources
- After data insertion, new entry resources should appear in the list

**Actual Behavior**:
- Only the single `queries` collection resource is ever listed
- No entry resources appear even after data insertion
- No individual query resources are exposed

## Root Cause Analysis

### 1. MCP Resource Implementation Issues

The `DrasiResourceType` class appears to have several problems:

1. **ListQueries Method**: Returns an empty list despite queries being initialized
2. **Resource Registration**: Individual queries aren't being registered as separate MCP resources
3. **Resource URI Handling**: The server expects certain URI patterns but doesn't properly handle them

### 2. Change Event Handler Not Connected

The `ChangeEventHandler` either:
1. Isn't properly subscribed to Dapr pubsub topics
2. Isn't receiving events due to configuration issues
3. Is receiving events but failing to process them

Evidence: No change event logs appear in the server output when database operations occur.

### 3. Resource Store State Management

The `ResourceStoreService` appears to be:
1. Not receiving updates from the change event handler
2. Not properly storing entry resources
3. Not making stored resources available via the MCP API

### 4. Configuration and Initialization Flow

There's a disconnect between:
1. Query configuration (which works - server knows about queries)
2. MCP resource exposure (which doesn't work - queries aren't exposed)
3. Change event processing (which doesn't work - no events processed)

## Technical Details from Testing

### SSE Response Format
The server returns responses in Server-Sent Events format:
```
event: message
data: {"result":{...},"id":1,"jsonrpc":"2.0"}
```

This required special parsing in tests but works correctly once handled.

### Deployment Configuration
The reaction uses a complex configuration with:
- Dapr sidecar for pubsub
- Dual port configuration (80 for Dapr, 8080 for MCP)
- Environment variable substitution for ports

### Error Patterns Observed
1. **"Unknown resource URI"**: Most common error, indicates resource doesn't exist
2. **"Entry not found"**: Server knows about the query but can't find the specific entry
3. **"An error occurred"**: Generic error for certain URI patterns, suggests deeper issues

## Recommendations for Fixes

### 1. Fix Query Resource Exposure
- Ensure `ListQueries()` returns the initialized queries
- Register each query as a separate MCP resource
- Update the `queries` collection to list available queries

### 2. Debug Change Event Processing
- Add extensive logging to `ChangeEventHandler`
- Verify Dapr pubsub subscription configuration
- Test pubsub connectivity independently
- Ensure event topic names match between publisher and subscriber

### 3. Fix Resource Store Integration
- Verify `ResourceStoreService` methods are called
- Add logging for all CRUD operations
- Ensure thread-safe access to the resource store
- Verify resource URIs are consistent

### 4. Improve Error Handling
- Add more specific error messages
- Log all resource access attempts
- Include debugging information in error responses

### 5. Add Integration Tests
- Test change event handling in isolation
- Test resource store operations directly
- Verify MCP resource registration

## Test Results Summary

| Test Case | Result | Issue |
|-----------|--------|-------|
| Health endpoint | ✅ PASS | - |
| List queries as MCP resources | ❌ FAIL | Returns empty list |
| Create entry on data insert | ❌ FAIL | Entry not created |
| Update entry on data update | ❌ FAIL | Timeout |
| Remove entry on data delete | ❌ FAIL | Timeout |
| Multiple queries independence | ❌ FAIL | No entries created |
| MCP SDK client usage | ⏭️ SKIPPED | Module compatibility issues |

## Conclusion

The MCP Server reaction has significant implementation issues that prevent it from functioning as a proper MCP server for Drasi data. The core problems are:

1. **Queries aren't exposed as MCP resources** despite being initialized
2. **Change events aren't processed**, so no data ever appears
3. **Resource URIs don't work** as expected

These issues need to be addressed in the C# implementation before the server can be used effectively. The test suite is comprehensive and will validate the fixes once implemented.