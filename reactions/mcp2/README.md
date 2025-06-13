# Drasi MCP Server Reaction

A Drasi reaction that exposes query results through the Model Context Protocol (MCP), enabling AI assistants and other MCP clients to access real-time Drasi query data.

## Overview

The MCP Server reaction maintains an in-memory view of Drasi query results and exposes them through a REST API following MCP resource patterns. It runs two services in a single container:

- **Port 80**: Drasi event listener (receives change events via Dapr)
- **Port 8080**: MCP server (serves resources to AI clients)

## Features

- **Dual-Service Architecture**: Separate Kestrel instances for Drasi and MCP
- **HTTP/SSE Transport**: Uses Server-Sent Events for real-time updates
- **Native Resource Support**: Implements MCP resources using the official SDK
- **Resource Discovery**: MCP clients can list available queries and their entries
- **Real-time Updates**: Resources update automatically when Drasi query results change
- **Two-level Hierarchy**: Organized structure for queries and their entries

## Architecture

### Dual-Service Design

The reaction uses ASP.NET Core's `IHostedService` pattern to run both services:

1. **Drasi Reaction Service** (Port 80)
   - Built using Drasi Reaction SDK
   - Receives change events from subscribed queries
   - Updates the in-memory resource store

2. **MCP Server** (Port 8080)
   - Separate Kestrel instance running as a hosted service
   - Exposes REST endpoints following MCP patterns
   - Reads from the shared in-memory store

## Resource URI Structure

The MCP server exposes resources using the following URI patterns, where `{reactionName}` is derived from the `REACTION_NAME` environment variable (defaulting to "mcp-server"):

- `drasi://{reactionName}/queries` - Lists all available queries
- `drasi://{reactionName}/queries/{queryId}` - Gets a specific query with its metadata and a list of its entry URIs
- `drasi://{reactionName}/queries/{queryId}/entries` - Lists all entries (metadata) for a query
- `drasi://{reactionName}/queries/{queryId}/entries/{entryId}` - Gets a specific entry's content

## Configuration

### Reaction Provider

The MCP Server reaction provider defines endpoints for both services:

```yaml
apiVersion: v1
kind: ReactionProvider
name: McpServer
spec:
  services:
    reaction:
      image: reaction-mcp-server
      dapr:
        app-port: "80"
      endpoints:
        dapr-http:
          port: 80
          protocol: "HTTP"
        mcp-server:
          port: 8080
          protocol: "HTTP"
  config_schema:
    type: object
    properties:
      keyField:
        type: string
        description: Field to use as unique identifier for resources
        default: "id"
      resourceContentType:
        type: string
        description: MIME type for resource content
        default: "application/json"
      description:
        type: string
        description: Description of the query exposed via MCP
    required: 
      - keyField
```

### Reaction Instance

Create a reaction instance with JSON-formatted query configurations:

```yaml
kind: Reaction
apiVersion: v1
name: mcp-server
spec:
  kind: McpServer
  queries:
    active-customers: |
      {
        "keyField": "customerId",
        "resourceContentType": "application/json",
        "description": "Active customer records from the CRM system"
      }
    inventory-levels: |
      {
        "keyField": "sku",
        "resourceContentType": "application/json",
        "description": "Real-time inventory levels across all warehouses"
      }
```

### Per-Query Configuration

Each query configuration must be valid JSON with these properties:

- `keyField` (required): Field in query results to use as unique identifier for entries
- `resourceContentType`: MIME type for resource content (default: "application/json")
- `description`: Human-readable description of the query data

## Building

### Prerequisites

- .NET 9.0 SDK
- Docker
- Make

### Build Docker Image

```bash
make docker-build
```

### Load into Kind Cluster

```bash
make kind-load
```

### Load into k3d Cluster

```bash
make k3d-load
```

## Endpoints

- **Port 80**: Dapr HTTP endpoint for receiving Drasi events
- **Port 8080**: MCP HTTP/SSE endpoint for client connections

### Available Endpoints

- `GET /health` - Health check endpoint
- `GET /mcp` - MCP server information
- `GET /mcp/sse` - SSE endpoint for MCP clients
- `POST /mcp` - Streamable HTTP endpoint for MCP requests

## Using with MCP Clients

### Claude Desktop

Add to your Claude desktop configuration:

```json
{
  "mcpServers": {
    "drasi": {
      "url": "http://localhost:8080/mcp/sse",
      "transport": "sse"
    }
  }
}
```

### Using with the MCP CLI

```bash
# List available resources
mcp list --server http://localhost:8080/mcp/sse

# Read a specific resource
mcp read --server http://localhost:8080/mcp/sse --uri "drasi://queries/customer-orders"
```

### Other MCP Clients

Connect to the SSE endpoint at `http://localhost:8080/mcp/sse` using any MCP-compatible client that supports HTTP/SSE transport.

## Development

### Prerequisites

- .NET 9.0 SDK
- Docker (for containerized deployment)
- Access to a Drasi deployment

### Building

```bash
dotnet build
```

### Running Locally

```bash
# Set environment variables
export REACTION_NAME=drasi-mcp-server
export ASPNETCORE_URLS="http://localhost:5000;http://localhost:8080"

# Run the application
dotnet run
```

### Running Tests

```bash
make test
```

### Clean Build Artifacts

```bash
make clean
```

## Environment Variables

- `REACTION_NAME`: Name of the reaction instance (default: "mcp-server")
- `ASPNETCORE_URLS`: URLs to listen on (default: "http://+:80;http://+:8080")
- `Logging__LogLevel__Default`: Default log level
- `Logging__LogLevel__Drasi_Reactions_McpServer`: Log level for MCP server
- `Logging__LogLevel__ModelContextProtocol`: Log level for MCP SDK

## Implementation Details

### Drasi SDK Integration

The reaction properly uses the Drasi Reaction SDK's `ReactionBuilder` pattern:

```csharp
var reaction = new ReactionBuilder<QueryConfig>()
    .UseChangeEventHandler<ChangeEventHandler>()
    .UseControlEventHandler<ControlSignalHandler>()
    .UseJsonQueryConfig()
    .ConfigureServices(services => {
        // Register services including MCP hosted service
    })
    .Build();

await reaction.StartAsync();
```

The SDK handles all Dapr integration including:
- Setting up Dapr pub/sub subscriptions
- Processing incoming events from Dapr
- Managing the application lifecycle
- Routing events to registered handlers

### Resource Attributes

The MCP resources are implemented using attributes from the ModelContextProtocol SDK:

- `[McpServerResourceType]`: Marks a class containing MCP resources
- `[McpServerResource]`: Marks a method as an MCP resource handler with relative URI templates

### URI Template Structure

The MCP server uses relative URI templates that are resolved by the MCP SDK:

- `queries` - Maps to the list of available queries
- `queries/{queryId}` - Maps to a specific query resource  
- `queries/{queryId}/entries` - Maps to entries for a specific query
- `queries/{queryId}/entries/{entryId}` - Maps to a specific entry

### Resource Methods

- `ListQueries()`: Returns all available Drasi queries
- `GetQuery(queryId)`: Returns a specific query with its entries
- `GetQueryEntries(queryId)`: Returns all entries for a query
- `GetQueryEntry(queryId, entryId)`: Returns a specific entry

### Real-time Updates

When Drasi sends change events:
1. The Drasi SDK receives events via Dapr on port 80
2. Events are routed to the `ChangeEventHandler`
3. The `ResourceStoreService` updates the in-memory store
4. The MCP server (port 8080) serves updated resources
5. Subscribed clients receive notifications via Server-Sent Events

## License

Copyright 2025 The Drasi Authors.

Licensed under the Apache License, Version 2.0.