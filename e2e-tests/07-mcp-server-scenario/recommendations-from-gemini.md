# Recommendations for Improving the MCP Server Reaction (mcp2)

This document outlines suggested fixes and best practices to enhance the `mcp2` Drasi reaction, drawing inspiration from the robust design of the `sync-statestore` reaction. The primary goals are to ensure correct MCP resource exposure and reliable processing of Drasi change events.

## 1. Address E2E Test Failures

The immediate priority is to fix the issues identified by the E2E tests:

### 1.1. Query Resources Not Exposed Correctly

**Problem:** The MCP server returns `{"queries": []}` when the `queries` collection resource is read, instead of listing individual query resources like `customer-data` and `order-data`.

**Likely Cause:** Failure to correctly deserialize `QueryConfig` in `QueryInitializationService.cs`, leading to `_resourceStore.AddQueryResource()` not being called for the configured queries.

**Suggested Fixes:**

*   **Enhance Logging in `QueryInitializationService.cs`:**
    Add detailed logging to confirm if `QueryConfig` objects are being deserialized successfully.
    ````csharp
    // filepath: /Users/aman/forks/platform/reactions/mcp2/Drasi.Reactions.McpServer/Services/QueryInitializationService.cs
    // ...existing code...
            foreach (var queryName in _queryConfigService.GetQueryNames())
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Initialization cancelled.");
                    break;
                }

                _logger.LogDebug("Initializing MCP resource for query: {QueryName}", queryName);
                var queryConfig = _queryConfigService.GetQueryConfig<Models.QueryConfig>(queryName);

                if (queryConfig == null)
                {
                    _logger.LogWarning("Query configuration for {QueryName} is null or failed to deserialize. Skipping MCP resource addition.", queryName);
                    // Consider logging the raw JSON string from _queryConfigService if possible for deeper debugging:
                    // var rawJson = _queryConfigService.GetRawQueryConfigJson(queryName); 
                    // _logger.LogDebug("Raw JSON for {QueryName}: {RawJson}", queryName, rawJson);
                    continue;
                }

                // Log the deserialized config to confirm its contents
                _logger.LogDebug("Deserialized QueryConfig for {QueryName}: KeyField='{KeyField}', ContentType='{ContentType}', Description='{Description}'",
                    queryName, queryConfig.KeyField, queryConfig.ResourceContentType, queryConfig.Description);

                _resourceStore.AddQueryResource(queryName, queryConfig);
                _logger.LogInformation("Added MCP resource for query: {QueryName}", queryName);
            }
    // ...existing code...
    ````
*   **Verify `QueryConfig.cs` Model:**
    Ensure `Drasi.Reactions.McpServer.Models.QueryConfig.cs` has public properties with correct `JsonPropertyName` attributes matching the JSON structure in `reaction.yaml`. (This appears to be correct currently).
    ````csharp
    // filepath: /Users/aman/forks/platform/reactions/mcp2/Drasi.Reactions.McpServer/Models/QueryConfig.cs
    using System.Text.Json.Serialization;

    namespace Drasi.Reactions.McpServer.Models;

    public class QueryConfig
    {
        [JsonPropertyName("keyField")]
        public string KeyField { get; set; } = string.Empty;

        [JsonPropertyName("resourceContentType")]
        public string ResourceContentType { get; set; } = "application/octet-stream";

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
    ````
*   **Review `DrasiResourceType.ListQueries()`:**
    Ensure the method responsible for serving the `drasi://{_reactionName}/queries` resource in `DrasiResourceType.cs` correctly fetches and serializes the list of query metadata from `IResourceStoreService`.

### 1.2. Change Events Not Processed

**Problem:** The `ChangeEventHandler` does not seem to process data changes from Drasi, and new/updated data is not reflected in MCP resources.

**Likely Cause:** Dapr `app-port` misconfiguration in `reaction-provider.yaml`. Dapr is likely attempting to deliver events to the MCP server port (8080) instead of the Drasi event listener port (80).

**Suggested Fix:**

*   **Correct `app-port` in `reaction-provider.yaml`:**
    Change `app-port` to the port your Drasi Reaction SDK's event listener is running on (typically port 80).
    ````yaml
    # filepath: /Users/aman/forks/platform/reactions/mcp2/reaction-provider.yaml
    # ...existing code...
    spec:
      services:
        reaction:
          # ...existing code...
          dapr:
            app-port: "80" # Changed from "8080"
            app-protocol: http
          config_schema:
            type: object
            properties:
              daprHttpPort:
                type: string
                default: "80"
              mcpServerPort:
                type: string
                default: "8080"
    # ...existing code...
    ````

## 2. Adopt Best Practices from `sync-statestore`

Incorporate the following robust practices observed in the `sync-statestore` reaction:

### 2.1. Robust Query Initialization (`QueryInitializationService.cs`)

*   **Wait for Dapr Sidecar:** Before attempting any Dapr-dependent operations (like fetching initial data if that were a requirement, or ensuring pub/sub subscriptions are active), ensure the Dapr sidecar is healthy. The Drasi Reaction SDK often handles Dapr client initialization, but explicit checks or delays in your hosted services might be beneficial if issues persist.
*   **Sequential Initialization:** Ensure that query configurations are fully loaded and processed by `QueryInitializationService` *before* the `McpServerHostedService` fully starts and begins serving requests. This prevents race conditions where MCP requests arrive before resources are defined.
*   **Detailed Logging:** Log each step of the query initialization: fetching names, deserializing config, adding to the resource store.

### 2.2. Configuration Validation (`ConfigValidationService.cs`)

*   **Implement `IConfigValidationService`:** Create a service that implements `Drasi.Reaction.SDK.Services.IConfigValidationService`.
*   **Validate Query Configurations:** In this service, validate the deserialized `QueryConfig` objects. Ensure required fields (like `keyField`, `resourceContentType`) are present and have valid values.
    ````csharp
    // Example for /Users/aman/forks/platform/reactions/mcp2/Drasi.Reactions.McpServer/Services/ConfigValidationService.cs
    // ... (using statements)
    public class ConfigValidationService : IConfigValidationService
    {
        private readonly ILogger<ConfigValidationService> _logger;

        public ConfigValidationService(ILogger<ConfigValidationService> logger)
        {
            _logger = logger;
        }

        public bool ValidateConfig(IConfiguration reactionConfiguration)
        {
            // Validate general reaction config if needed
            _logger.LogInformation("Performing general reaction configuration validation (if any).");
            return true;
        }

        public bool ValidateQueryConfig<TQueryConfig>(string queryName, TQueryConfig? queryConfig) where TQueryConfig : class
        {
            if (queryConfig == null)
            {
                _logger.LogError("Validation failed for query {QueryName}: Configuration is null.", queryName);
                return false;
            }

            if (queryConfig is Models.QueryConfig mcpQueryConfig)
            {
                if (string.IsNullOrWhiteSpace(mcpQueryConfig.KeyField))
                {
                    _logger.LogError("Validation failed for query {QueryName}: 'keyField' is missing or empty.", queryName);
                    return false;
                }
                if (string.IsNullOrWhiteSpace(mcpQueryConfig.ResourceContentType))
                {
                    _logger.LogError("Validation failed for query {QueryName}: 'resourceContentType' is missing or empty.", queryName);
                    return false;
                }
                _logger.LogInformation("Query configuration for {QueryName} validated successfully.", queryName);
                return true;
            }

            _logger.LogWarning("Validation for query {QueryName} skipped: Configuration is not of expected type McpServer.Models.QueryConfig.", queryName);
            return true; // Or false if this is an error condition
        }
    }
    ````
*   **Register in `Program.cs`:**
    ````csharp
    // filepath: /Users/aman/forks/platform/reactions/mcp2/Drasi.Reactions.McpServer/Program.cs
    // ...existing code...
    using Drasi.Reactions.McpServer.Services; // For ConfigValidationService
    using Drasi.Reaction.SDK.Services; // For IConfigValidationService

    // ...
    builder.Services.AddReactionFramework()
        .AddQueryConfigService()
        .AddChangeEventHandler<ChangeEventHandler>()
        .AddControlSignalHandler<ControlSignalHandler>()
        .AddConfigValidationService<ConfigValidationService>() // Add this line
        .AddInitializationService<QueryInitializationService>();
    // ...existing code...
    ````

### 2.3. Startup Orchestration and Service Dependencies

*   **Hosted Services Order:** While .NET Core doesn't guarantee `IHostedService` start order, design services to be resilient. `QueryInitializationService` should complete its critical work (populating `ResourceStoreService`) before `McpServerHostedService` relies on that data. Consider using events, shared readiness flags, or ensuring `QueryInitializationService` runs to completion in its `StartAsync` before other services proceed if strict ordering is critical.
*   **Dapr Client Readiness:** Ensure the Dapr client (provided by the SDK or initialized manually) is ready before services that use it (like `ChangeEventHandler` or any service making Dapr calls) become active.

### 2.4. Comprehensive Logging

*   **Contextual Logging:** Use structured logging and include relevant context (e.g., `QueryName`, `EntryId`, `MCP-RequestId`) in log messages.
*   **Log Critical Paths:** Ensure logging at the beginning and end of key operations, and for all error conditions.
*   **Log Dapr Interactions:** Log when events are received from Dapr and when calls are made to Dapr.

### 2.5. Graceful Error Handling

*   **Resilience:** Implement retry mechanisms for transient errors when interacting with external services (though Dapr often handles this for state stores/pub-sub).
*   **Clear Error Reporting:** When errors occur (e.g., failed deserialization, inability to process an event), log them clearly and, if appropriate, expose a meaningful error through the MCP interface.

## 3. MCP Resource Exposure Best Practices (`DrasiResourceType.cs`)

*   **Clear URI Structure:** The current URI structure (`drasi://{_reactionName}/queries/{queryId}/entries/{entryId}`) is good. Ensure it's consistently applied.
*   **Correct Content Types:** Use the `resourceContentType` from the `QueryConfig` for individual entry resources. For collection resources (like the list of queries or list of entries), `application/json` is appropriate.
*   **Idempotency:** Ensure that MCP operations that should be idempotent (like `resources/get`) are indeed so.
*   **Resource Metadata:** When listing resources (e.g., in the content of the `.../queries` resource), provide sufficient metadata for clients to understand what each resource represents (e.g., name, description, URI, content type). The `McpResourceMetadata` class from the MCP SDK is designed for this.

By implementing these fixes and adopting these best practices, the `mcp2` reaction will become more robust, reliable, and easier to maintain, aligning well with the quality standards demonstrated by other Drasi reactions.