# Drasi MCP Server Reaction

A Drasi reaction that exposes query results via Model Context Protocol (MCP) patterns, enabling AI applications to consume real-time data from Drasi queries.

## Overview

The MCP Server reaction maintains an in-memory view of Drasi query results and exposes them through a REST API following MCP resource patterns. It runs two services in a single container:

- **Port 80**: Drasi event listener (receives change events via Dapr)
- **Port 8080**: MCP server (serves resources to AI clients)

## Architecture

### Dual-Service Design

The reaction uses ASP.NET Core's `IHostedService` pattern to run both services:

1. **Drasi Reaction Service** (Port 80)
   - Built using Drasi Reaction SDK
   - Receives change events from subscribed queries
   - Updates the in-memory resource store
   - Tracks sequence numbers to prevent duplicate processing

2. **MCP Server** (Port 8080)
   - Separate Kestrel instance
   - Exposes REST endpoints following MCP patterns
   - Reads from the shared in-memory store

### Resource Model

Resources follow a two-level hierarchy:

- **Query Level**: `drasi://{reaction-name}/queries/{query-id}`
  - Lists available queries with metadata
  - Reading returns list of entry URIs

- **Entry Level**: `drasi://{reaction-name}/entries/{query-id}/{entry-key}`
  - Individual query result entries
  - Contains the actual data

### Startup Sequence

The reaction follows a three-phase startup pattern:

1. **Configuration Validation**
   - Validates all query configurations
   - Ensures required fields (keyField) are present
   - Fails fast on invalid configuration

2. **Query Readiness Check**
   - Waits up to 5 minutes for each query to be ready
   - Uses extended management client to poll query status
   - Prevents initialization of queries that aren't running

3. **Initial Data Sync**
   - Fetches current results from ResultViewClient
   - Extracts sync point from result stream header
   - Populates in-memory resource store
   - Initializes sequence tracking

### Sequence Tracking

The reaction uses sync points to ensure exactly-once processing during runtime:

- **Sync Point Manager**: Tracks last processed sequence per query
- **Duplicate Prevention**: Skips events with sequence ≤ last processed
- **Atomic Updates**: Only updates sync point after successful processing

**Important**: Sync points are stored in-memory and are lost on restart. The reaction will re-fetch all data after a restart.

## Configuration

### Reaction Provider

Register the MCP Server provider with Drasi:

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
```

### Reaction Instance

Create a reaction instance to expose specific queries:

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

### Query Configuration Options

- `keyField` (required): Field in query results to use as unique identifier
- `resourceContentType`: MIME type for resources (default: "application/json")
- `description`: Human-readable description of the query data

## API Endpoints

The MCP server exposes the following endpoints on port 8080:

### GET /
Server information and available endpoints

### GET /health
Health check endpoint

### GET /resources
List all available resources (queries and entries)

**Response Example:**
```json
[
  {
    "uri": "drasi://mcp-server/queries/active-customers",
    "name": "Query: active-customers",
    "description": "Active customer records from the CRM system",
    "mimeType": "application/json"
  }
]
```

### GET /resources/read?uri={uri}
Read a specific resource by URI

**Query Resource Response:**
```json
{
  "uri": "drasi://mcp-server/queries/active-customers",
  "mimeType": "application/json",
  "text": "{\"queryId\":\"active-customers\",\"entryCount\":150,\"entries\":[...]}"
}
```

**Entry Resource Response:**
```json
{
  "uri": "drasi://mcp-server/entries/active-customers/CUST-001",
  "mimeType": "application/json",
  "text": "{\"customerId\":\"CUST-001\",\"name\":\"Acme Corp\",\"status\":\"active\"}"
}
```

## Building and Deployment

### Build Docker Image

```bash
make docker-build
```

Or manually:

```bash
docker build -t drasi-project/reaction-mcp-server:latest .
```

### Load to Kubernetes

For Kind:
```bash
make kind-load
```

For k3d:
```bash
make k3d-load
```

### Deploy to Drasi

1. Register the reaction provider:
```bash
kubectl apply -f reaction-provider.yaml
```

2. Create a reaction instance:
```bash
kubectl apply -f reaction.yaml
```

## Development

### Prerequisites

- .NET 9.0 SDK
- Docker
- Kubernetes cluster with Drasi installed

### Running Tests

```bash
make test
```

### Project Structure

```
Drasi.Reactions.Mcp/
├── Program.cs                      # Main entry point
├── Services/
│   ├── McpServerHostedService.cs   # MCP server implementation
│   ├── McpChangeHandler.cs         # Processes Drasi events
│   ├── InMemoryMcpResourceStore.cs # Shared resource storage
│   ├── McpResourceProvider.cs      # MCP resource operations
│   └── QueryInitializationService.cs # Initial data sync
├── Models/
│   ├── McpQueryConfig.cs           # Query configuration
│   ├── McpResource.cs              # Internal resource model
│   └── DrasiResource.cs            # MCP resource format
└── Interfaces/
    ├── IMcpResourceStore.cs        # Resource storage interface
    └── IMcpNotifier.cs             # Notification interface
```

### Key Components

1. **McpServerHostedService**: Runs the MCP server as a background service
2. **McpChangeHandler**: Processes change events with sequence tracking
3. **InMemoryMcpResourceStore**: Thread-safe storage for resources
4. **QueryInitializationService**: Waits for queries and syncs initial data
5. **McpResourceProvider**: Implements resource listing and reading
6. **ExtendedManagementClient**: Checks query readiness with timeout
7. **McpSyncPointManager**: Tracks processed sequences per query
8. **ErrorStateHandler**: Handles fatal errors with graceful shutdown

## Environment Variables

- `APP_PORT`: Drasi event listener port (default: 80)
- `MCP_PORT`: MCP server port (default: 8080)
- `REACTION_NAME`: Name used in resource URIs (default from Drasi)

## Limitations

- **In-Memory Storage**: All data (resources and sync points) lost on restart
  - Resources are not persisted to disk/database
  - Sync points are not persisted
  - **Cannot resume from last sequence** - will re-fetch all data after restart
- **No Authentication**: MCP endpoints are unauthenticated
- **REST API**: Currently implements REST endpoints; full MCP SDK integration pending

## Future Enhancements

1. **Persistent Storage**: Add Redis or database backing
2. **MCP SDK Integration**: Use official MCP SDK when available
3. **WebSocket Support**: Real-time notifications to MCP clients
4. **Authentication**: Add security to MCP endpoints
5. **Resource Filtering**: Query parameters for resource listing

## Troubleshooting

### MCP Server Not Accessible

1. Ensure port 8080 is exposed in Kubernetes Service
2. Check pod logs for startup errors
3. Verify health endpoint: `http://<pod-ip>:8080/health`

### Missing Initial Data

1. Check QueryInitializationService logs
2. Verify Drasi management API is accessible
3. Ensure queries have current results

### Resource Not Found

1. Verify resource URI format
2. Check if query is configured in reaction
3. Ensure keyField exists in query results

## Contributing

This reaction follows standard Drasi patterns. See the main Drasi documentation for contribution guidelines.