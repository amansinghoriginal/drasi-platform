# Drasi MCP Query Results Server

The MCP Query Results Server is a Drasi Reaction that exposes continuous query results as resources through the Model Context Protocol (MCP). It enables AI assistants and other MCP clients to access real-time data from Drasi queries, bridging the gap between continuous queries and AI applications.

## Overview

This reaction maintains an in-memory store of query results and serves them via MCP's resource protocol. It provides two levels of access:
- **Query-level resources**: List all entries in a query result set
- **Entry-level resources**: Access individual entries from query results

As Drasi detects changes in your data sources, this reaction automatically updates the exposed resources and notifies connected MCP clients through the standard MCP notification system.

## Key Features

- **Real-time Updates**: Automatically reflects changes from Drasi continuous queries
- **MCP-Compliant**: Fully implements the MCP resource protocol with proper notifications
- **Flexible Content Types**: Supports JSON, plain text, and XML resource formats
- **Session Management**: Tracks active MCP client sessions for targeted notifications
- **Error Handling**: Robust error handling with proper MCP error responses

## Architecture

The reaction consists of several components:

1. **Drasi Integration** (Dapr-based)
   - Receives change events from continuous queries
   - Processes control signals for query lifecycle management

2. **MCP Server** (HTTP/SSE transport)
   - Exposes resources at `/mcp` endpoint
   - Handles client subscriptions and notifications
   - Supports both SSE for server-initiated messages and HTTP POST for client requests

3. **Query Result Store**
   - Maintains current state of all query results in memory
   - Applies incremental updates from change events
   - Provides fast lookups for MCP resource requests

## Configuration

### Reaction Configuration

When deploying the reaction, you can configure it using the following YAML:

```yaml
apiVersion: v1
kind: Reaction  
name: mcp-query-results
spec:
  kind: McpQueryResultsServer
  properties:
    mcpServerPort: "8080"  # Port for MCP server (default: 8080)
    daprHttpPort: "80"     # Port for Dapr HTTP (default: 80)
  queries:
    my-query:
      keyField: "id"                              # Required: Field to use as unique key
      resourceContentType: "application/json"      # Optional: MIME type (default: application/json)
      description: "Description of query data"     # Optional: Human-readable description
```

### Query Configuration Properties

- **keyField** (required): The field in your query results that uniquely identifies each entry
- **resourceContentType**: MIME type for resource content. Supported values:
  - `application/json` (default)
  - `text/plain`
  - `application/xml`
- **description**: Optional description shown in query-level resources

## Resource URIs

The reaction exposes resources using the following URI scheme:

### Query-Level Resources
```
drasi://queries/{query-id}
```
Returns metadata about the query and a list of all entry URIs.

Example response:
```json
{
  "queryId": "inventory-status",
  "description": "Real-time inventory levels",
  "contentType": "application/json",
  "entryCount": 42,
  "entries": [
    "drasi://entries/inventory-status/item-001",
    "drasi://entries/inventory-status/item-002"
  ]
}
```

### Entry-Level Resources
```
drasi://entries/{query-id}/{entry-key}
```
Returns the actual data for a specific entry.

## MCP Client Integration

### Claude Desktop Configuration

To use this reaction with Claude Desktop, add it to your MCP settings:

```json
{
  "mcpServers": {
    "drasi-inventory": {
      "url": "http://localhost:8080/mcp",
      "transport": "sse"
    }
  }
}
```

### Programmatic Access (TypeScript)

```typescript
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { SSEClientTransport } from "@modelcontextprotocol/sdk/client/sse.js";

// Create MCP client
const transport = new SSEClientTransport(
  new URL("http://localhost:8080/mcp")
);
const client = new Client({
  name: "my-app",
  version: "1.0.0"
}, {
  capabilities: {}
});

// Connect and initialize
await client.connect(transport);
const result = await client.initialize();

// List available resources
const resources = await client.listResources();

// Read a specific query's entries
const queryData = await client.readResource({
  uri: "drasi://queries/inventory-status"
});

// Subscribe to changes
await client.subscribeToResource({
  uri: "drasi://queries/inventory-status"
});
```

## Example Use Cases

### 1. Real-Time Inventory Assistant

Expose inventory levels to an AI assistant:

```yaml
queries:
  low-stock-items:
    keyField: "sku"
    description: "Items with stock below reorder threshold"
```

The AI can then answer questions like "What items are running low?" with current data.

### 2. Customer Status Monitoring

Track customer account statuses:

```yaml
queries:
  vip-customers:
    keyField: "customerId"
    description: "High-value customers with recent activity"
```

### 3. System Health Dashboard

Monitor system components:

```yaml
queries:
  unhealthy-services:
    keyField: "serviceId"
    resourceContentType: "text/plain"
    description: "Services experiencing issues"
```

## Development

### Building the Reaction

```bash
# Build the Docker image
make docker-build

# Run tests
dotnet test

# Build for local development
dotnet build
```

### Running Locally

1. Set required environment variables:
```bash
export mcpServerPort=8080
```

2. Run the reaction:
```bash
dotnet run --project Drasi.Reactions.McpQueryResultsServer
```

### Testing with MCP Inspector

Use the MCP Inspector to test your reaction:

```bash
npx @modelcontextprotocol/inspector \
  --url http://localhost:8080/mcp \
  --transport sse
```

## Troubleshooting

### Common Issues

1. **"Query not found" errors**
   - Ensure the query is configured in the reaction's YAML
   - Verify the keyField exists in your query results

2. **Connection refused**
   - Check that mcpServerPort is not already in use
   - Verify the reaction is running and healthy

3. **No notifications received**
   - Ensure clients are properly subscribing to resources
   - Check logs for notification delivery errors

### Debug Logging

Enable debug logging by setting the log level:

```bash
export ASPNETCORE_LOGGING__LOGLEVEL__DEFAULT=Debug
```

## Security Considerations

- The MCP server exposes query data without authentication by default
- In production, deploy behind a reverse proxy with authentication
- Consider network policies to restrict access to the MCP port
- Sensitive data should be filtered at the query level, not in the reaction

## Performance Notes

- Query results are stored in memory - monitor memory usage for large result sets
- Consider implementing pagination for queries returning many entries
- The reaction processes changes incrementally for efficiency
- MCP notifications are sent only to subscribed clients

## Contributing

See the main Drasi platform documentation for contribution guidelines. Key areas for enhancement:

- Add support for pagination in large result sets
- Implement resource filtering/searching
- Add authentication mechanisms
- Support for binary content types
- Persistent storage option for query results

## License

Copyright 2025 The Drasi Authors. Licensed under the Apache License, Version 2.0.