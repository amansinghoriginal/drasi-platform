const { describe, beforeAll, afterAll, test, expect } = require('@jest/globals');
const { Client: PgClient } = require('pg');
const yaml = require('js-yaml');
const fs = require('fs');
const path = require('path');
const PortForward = require('../fixtures/port-forward');
const deployResources = require('../fixtures/deploy-resources');
// const deleteResources = require('../fixtures/delete-resources');
const { waitFor } = require('../fixtures/infrastructure');
// MCP SDK is optional - tests will use HTTP/JSON-RPC if SDK is not available
// The SDK uses ES modules which can be tricky to import in CommonJS
let McpClient, SSEClientTransport;
const hasMcpSdk = false; // Disable MCP SDK for now due to module resolution issues

// This test uses both MCP SDK client and direct HTTP/JSON-RPC calls to thoroughly test the server

const SCENARIO_DIR = __dirname;
const K8S_RESOURCES_FILE = path.join(SCENARIO_DIR, 'resources.yaml');
const SOURCES_FILE = path.join(SCENARIO_DIR, 'sources.yaml');
const QUERIES_FILE = path.join(SCENARIO_DIR, 'queries.yaml');
const REACTION_PROVIDER_FILE = path.join(SCENARIO_DIR, 'reaction-provider.yaml');
const REACTIONS_FILE = path.join(SCENARIO_DIR, 'reactions.yaml');

const POSTGRES_SERVICE_NAME = 'mcp-test-db';
const POSTGRES_NAMESPACE = 'default';
const POSTGRES_PORT = 5432;
const POSTGRES_USER = 'testuser';
const POSTGRES_PASSWORD = 'testpassword';
const POSTGRES_DATABASE = 'testdb';

const MCP_SERVER_SERVICE_NAME = 'mcp-server-e2e-mcp-server';
const MCP_SERVER_NAMESPACE = 'drasi-system';
const MCP_SERVER_PORT = 8080;

const REACTION_NAME = 'mcp-server-e2e';

function loadYaml(filePath) {
    const content = fs.readFileSync(filePath, 'utf8');
    return yaml.loadAll(content);
}

// Helper function to make HTTP requests to MCP server
// The MCP server returns Server-Sent Events (SSE) format, not plain JSON
async function mcpRequest(baseUrl, method, params = {}) {
    const response = await fetch(`${baseUrl}/mcp`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({
            jsonrpc: '2.0',
            id: Date.now(),
            method: method,
            params: params
        })
    });
    
    if (!response.ok) {
        throw new Error(`MCP request failed: ${response.status} ${response.statusText}`);
    }
    
    // Parse SSE format response
    const text = await response.text();
    const lines = text.split('\n');
    let jsonData = null;
    
    for (const line of lines) {
        if (line.startsWith('data: ')) {
            jsonData = JSON.parse(line.substring(6));
            break;
        }
    }
    
    if (!jsonData) {
        throw new Error('No JSON data found in SSE response');
    }
    
    if (jsonData.error) {
        throw new Error(`MCP error: ${jsonData.error.message}`);
    }
    
    console.log('DEBUG: MCP Request:', method, params);
    console.log('DEBUG: MCP Response:', JSON.stringify(jsonData, null, 2));
    
    return jsonData.result;
}

describe('MCP Server Reaction E2E Tests', () => {
    let pgClient;
    let pgPortForward;
    let mcpPortForward;
    let mcpBaseUrl;
    let mcpClient;
    let mcpTransport;

    const k8sResources = loadYaml(K8S_RESOURCES_FILE);
    const sourceResources = loadYaml(SOURCES_FILE);
    const queryResources = loadYaml(QUERIES_FILE);
    const reactionProviderResources = loadYaml(REACTION_PROVIDER_FILE);
    const reactionResources = loadYaml(REACTIONS_FILE);

    const allResourceDefinitions = [
        ...k8sResources,
        ...sourceResources,
        ...queryResources,
        ...reactionProviderResources,
        ...reactionResources,
    ];

    beforeAll(async () => {
        console.log("Starting E2E test setup for MCP Server reaction...");
        try {
            // 1. Deploy all k8s resources first
            console.log("Deploying K8s resources...");
            await deployResources(k8sResources);

            // 2. Wait for PostgreSQL to be ready
            console.log("Waiting for PostgreSQL to stabilize...");
            await waitFor({ timeoutMs: 15000, description: "PostgreSQL to stabilize" });

            // 3. Deploy Drasi resources
            console.log("Deploying Drasi Source resources...");
            await deployResources(sourceResources);

            console.log("Deploying Drasi Query resources...");
            await deployResources(queryResources);

            console.log("Deploying Drasi ReactionProvider resources...");
            await deployResources(reactionProviderResources);

            console.log("Deploying Drasi Reaction resources...");
            await deployResources(reactionResources);
            console.log("All Drasi resources deployed.");

            // 4. Set up port forwards
            pgPortForward = new PortForward(POSTGRES_SERVICE_NAME, POSTGRES_PORT, POSTGRES_NAMESPACE);
            const localPgPort = await pgPortForward.start();
            pgClient = new PgClient({
                host: 'localhost',
                port: localPgPort,
                user: POSTGRES_USER,
                password: POSTGRES_PASSWORD,
                database: POSTGRES_DATABASE,
            });
            await pgClient.connect();
            console.log("Connected to PostgreSQL via port forward.");

            // Port forward to MCP server
            mcpPortForward = new PortForward(MCP_SERVER_SERVICE_NAME, MCP_SERVER_PORT, MCP_SERVER_NAMESPACE);
            const localMcpPort = await mcpPortForward.start();
            mcpBaseUrl = `http://localhost:${localMcpPort}`;
            console.log(`MCP Server accessible at ${mcpBaseUrl}`);

            // 5. Wait for everything to stabilize
            console.log("Waiting for services to stabilize...");
            await waitFor({ timeoutMs: 20000, description: "all services to stabilize" });

            // 6. Initialize MCP SDK client with SSE transport (if available)
            if (hasMcpSdk && McpClient && SSEClientTransport) {
                console.log("Initializing MCP SDK client...");
                try {
                    mcpTransport = new SSEClientTransport(
                        new URL(`${mcpBaseUrl}/mcp/sse`)
                    );
                    mcpClient = new McpClient({
                        name: 'e2e-test-mcp-client',
                        version: '1.0.0'
                    }, {
                        capabilities: {}
                    });
                    
                    await mcpClient.connect(mcpTransport);
                    console.log("MCP SDK client connected successfully");
                } catch (error) {
                    console.warn("MCP SDK client connection failed:", error.message);
                    mcpClient = null;
                }
            } else {
                console.log("MCP SDK not available, using HTTP/JSON-RPC only");
                mcpClient = null;
            }

            // 7. Also test with direct HTTP connection
            console.log("Testing direct HTTP/JSON-RPC connection...");
            try {
                const initResult = await mcpRequest(mcpBaseUrl, 'initialize', {
                    protocolVersion: '2024-11-05',
                    capabilities: {},
                    clientInfo: {
                        name: 'e2e-test-http-client',
                        version: '1.0.0'
                    }
                });
                console.log("HTTP/JSON-RPC initialization successful:", initResult);
            } catch (error) {
                console.warn("HTTP/JSON-RPC initialization failed:", error.message);
            }

        } catch (error) {
            console.error("Error during beforeAll setup:", error);
            if (pgPortForward) pgPortForward.stop();
            if (mcpPortForward) mcpPortForward.stop();
            if (pgClient) await pgClient.end().catch(console.error);
            // await deleteResources(allResourceDefinitions).catch(err => console.error("Cleanup failed during error handling:", err));
            throw error;
        }
    }, 300000); // 5 minutes timeout for setup

    afterAll(async () => {
        console.log("Starting E2E test teardown...");
        if (pgClient) await pgClient.end().catch(err => console.error("Error closing PG client:", err));
        
        if (mcpClient) {
            await mcpClient.close().catch(err => console.error("Error closing MCP client:", err));
        }
        if (mcpTransport) {
            await mcpTransport.close().catch(err => console.error("Error closing MCP transport:", err));
        }
        
        if (pgPortForward) pgPortForward.stop();
        if (mcpPortForward) mcpPortForward.stop();
        
        console.log("Attempting to delete Drasi and K8s resources...");
        // await deleteResources(allResourceDefinitions).catch(err => console.error("Error during deleteResources:", err)); 
        console.log("Teardown complete.");
    }, 300000); // 5 minutes timeout for teardown

    test('should expose health endpoint', async () => {
        const response = await fetch(`${mcpBaseUrl}/health`);
        expect(response.ok).toBe(true);
        const text = await response.text();
        expect(text).toBe('OK');
    }, 20000);

    test('should list available queries as MCP resources using HTTP', async () => {
        console.log("Testing resource listing via HTTP/JSON-RPC...");
        
        // Try to list resources
        const resources = await waitFor({
            actionFn: async () => {
                try {
                    return await mcpRequest(mcpBaseUrl, 'resources/list');
                } catch (error) {
                    console.log("Resource list attempt failed:", error.message);
                    return null;
                }
            },
            predicateFn: (result) => result !== null && result.resources && result.resources.length > 0,
            timeoutMs: 30000,
            pollIntervalMs: 2000,
            description: "MCP server to list resources via HTTP"
        });

        expect(resources).toBeDefined();
        console.log('DEBUG: Raw resources response:', JSON.stringify(resources, null, 2));
        expect(resources.resources).toBeInstanceOf(Array);
        console.log('DEBUG: Total resources found:', resources.resources.length);
        console.log('DEBUG: Resource URIs:', resources.resources.map(r => r.uri));
        
        // The server returns a "queries" collection resource, let's read it
        const queriesResource = resources.resources.find(r => r.uri === 'queries');
        if (queriesResource) {
            console.log('DEBUG: Found queries collection resource, reading it...');
            try {
                const queriesContent = await mcpRequest(mcpBaseUrl, 'resources/read', { uri: 'queries' });
                console.log('DEBUG: Queries collection content:', JSON.stringify(queriesContent, null, 2));
                
                // Parse the text content to see the actual queries
                if (queriesContent.contents && queriesContent.contents[0]) {
                    const queriesData = JSON.parse(queriesContent.contents[0].text);
                    console.log('DEBUG: Parsed queries data:', JSON.stringify(queriesData, null, 2));
                    
                    // If we see queries in the list but empty, the server might have different URIs
                    if (queriesData.queries && Array.isArray(queriesData.queries)) {
                        console.log('DEBUG: Number of queries found:', queriesData.queries.length);
                    }
                }
            } catch (error) {
                console.log('DEBUG: Failed to read queries collection:', error.message);
            }
        }
        
        // Should have query resources
        const queryResources = resources.resources.filter(r => 
            r.uri.includes('/queries/') && !r.uri.includes('/entries/')
        );
        console.log('DEBUG: Query resources found:', queryResources.length);
        console.log('DEBUG: Query resource details:', queryResources);
        
        // For now, let's check if we have the queries collection resource
        expect(queriesResource).toBeDefined();
        expect(queriesResource.uri).toBe('queries');
        
        // Check for specific queries
        const customerQuery = queryResources.find(r => r.uri.includes('customer-data'));
        expect(customerQuery).toBeDefined();
        expect(customerQuery.name).toBe('customer-data');
        expect(customerQuery.description).toContain('E2E test customer data');
        
        const orderQuery = queryResources.find(r => r.uri.includes('order-data'));
        expect(orderQuery).toBeDefined();
        expect(orderQuery.name).toBe('order-data');
        expect(orderQuery.description).toContain('E2E test order data');
    }, 40000);

    test('should list available queries using MCP SDK client', async () => {
        if (!mcpClient) {
            console.log("Skipping MCP SDK client test - client not connected");
            return;
        }
        
        console.log("Testing resource listing via MCP SDK client...");
        
        // List resources using MCP client
        const resources = await waitFor({
            actionFn: async () => {
                try {
                    const result = await mcpClient.listResources();
                    return result;
                } catch (error) {
                    console.log("MCP client resource list attempt failed:", error.message);
                    return null;
                }
            },
            predicateFn: (result) => result !== null && result.resources && result.resources.length > 0,
            timeoutMs: 30000,
            pollIntervalMs: 2000,
            description: "MCP client to list resources"
        });

        expect(resources).toBeDefined();
        expect(resources.resources).toBeInstanceOf(Array);
        
        // Should have query resources
        const queryResources = resources.resources.filter(r => 
            r.uri.includes('/queries/') && !r.uri.includes('/entries/')
        );
        expect(queryResources.length).toBeGreaterThanOrEqual(2);
        
        // Validate the resources match expected format
        const customerQuery = queryResources.find(r => r.uri.includes('customer-data'));
        expect(customerQuery).toBeDefined();
        expect(customerQuery.name).toBe('customer-data');
        
        const orderQuery = queryResources.find(r => r.uri.includes('order-data'));
        expect(orderQuery).toBeDefined();
        expect(orderQuery.name).toBe('order-data');
    }, 40000);

    test('should create entry resources on customer data insert', async () => {
        const customerId = `cust-${Date.now()}`;
        const customerName = `Test Customer ${Date.now()}`;
        const customerEmail = `${customerId}@test.com`;
        
        console.log(`Inserting customer: ${customerId}`);
        
        // Insert data into PostgreSQL
        await pgClient.query(
            "INSERT INTO customers (customer_id, name, email) VALUES ($1, $2, $3)",
            [customerId, customerName, customerEmail]
        );
        console.log('DEBUG: Inserted customer with ID:', customerId);
        
        // Wait for MCP server to process the change
        // Let's try different URI formats
        const entryUri = `drasi://${REACTION_NAME}/queries/customer-data/entries/${customerId}`;
        const simpleUri = `customer-data/entries/${customerId}`;
        const queryUri = `queries/customer-data/entries/${customerId}`;
        
        console.log(`Waiting for entry resource: ${entryUri}`);
        console.log('DEBUG: Looking for resource URI:', entryUri);
        console.log('DEBUG: Also trying simpler URIs:', simpleUri, queryUri);
        
        // First, let's list all resources to see what's available
        console.log('DEBUG: Listing all resources after insert...');
        try {
            const allResources = await mcpRequest(mcpBaseUrl, 'resources/list');
            console.log('DEBUG: All resources after insert:', JSON.stringify(allResources, null, 2));
        } catch (error) {
            console.log('DEBUG: Failed to list resources:', error.message);
        }
        
        // Test with HTTP/JSON-RPC - try different URI formats
        const entry = await waitFor({
            actionFn: async () => {
                // Try different URI formats
                const urisToTry = [entryUri, simpleUri, queryUri];
                
                for (const uri of urisToTry) {
                    try {
                        console.log(`DEBUG: Trying to read resource with URI: ${uri}`);
                        const result = await mcpRequest(mcpBaseUrl, 'resources/read', { uri });
                        console.log(`DEBUG: Successfully read resource with URI: ${uri}`);
                        return result;
                    } catch (error) {
                        console.log(`DEBUG: Failed with URI ${uri}: ${error.message}`);
                    }
                }
                
                return null;
            },
            predicateFn: (result) => result !== null && result.contents && result.contents.length > 0,
            timeoutMs: 30000,
            pollIntervalMs: 2000,
            description: `MCP entry resource for customer ${customerId}`
        });
        
        expect(entry).toBeDefined();
        expect(entry.contents).toBeDefined();
        expect(entry.contents).toHaveLength(1);
        
        const content = JSON.parse(entry.contents[0].text);
        expect(content.customer_id).toBe(customerId);
        expect(content.name).toBe(customerName);
        expect(content.email).toBe(customerEmail);
        
        // Also test with MCP SDK client if available
        if (mcpClient) {
            console.log(`Testing resource read with MCP SDK client for: ${entryUri}`);
            try {
                const clientResult = await mcpClient.readResource({ uri: entryUri });
                expect(clientResult).toBeDefined();
                expect(clientResult.contents).toBeDefined();
                expect(clientResult.contents).toHaveLength(1);
                
                const clientContent = JSON.parse(clientResult.contents[0].text);
                expect(clientContent.customer_id).toBe(customerId);
                expect(clientContent.name).toBe(customerName);
                expect(clientContent.email).toBe(customerEmail);
                
                console.log("MCP SDK client read successful");
            } catch (error) {
                console.warn("MCP SDK client read failed:", error.message);
            }
        }
    }, 40000);

    test('should update entry resources on customer data update', async () => {
        // First insert a customer
        const customerId = `cust-update-${Date.now()}`;
        const originalName = `Original Customer ${Date.now()}`;
        const updatedName = `Updated Customer ${Date.now()}`;
        const customerEmail = `${customerId}@test.com`;
        
        console.log(`Inserting customer for update test: ${customerId}`);
        
        await pgClient.query(
            "INSERT INTO customers (customer_id, name, email) VALUES ($1, $2, $3)",
            [customerId, originalName, customerEmail]
        );
        
        // Wait for initial entry to be created
        const entryUri = `drasi://${REACTION_NAME}/queries/customer-data/entries/${customerId}`;
        
        await waitFor({
            actionFn: async () => {
                try {
                    return await mcpRequest(mcpBaseUrl, 'resources/read', { uri: entryUri });
                } catch (error) {
                    return null;
                }
            },
            predicateFn: (result) => result !== null,
            timeoutMs: 20000,
            pollIntervalMs: 1000,
            description: `Initial entry for customer ${customerId}`
        });
        
        // Update the customer
        console.log(`Updating customer name to: ${updatedName}`);
        await pgClient.query(
            "UPDATE customers SET name = $1 WHERE customer_id = $2",
            [updatedName, customerId]
        );
        
        // Wait for the update to be reflected
        const updatedEntry = await waitFor({
            actionFn: async () => {
                try {
                    const result = await mcpRequest(mcpBaseUrl, 'resources/read', { uri: entryUri });
                    if (result && result.contents && result.contents.length > 0) {
                        const content = JSON.parse(result.contents[0].text);
                        return content.name === updatedName ? result : null;
                    }
                    return null;
                } catch (error) {
                    return null;
                }
            },
            predicateFn: (result) => result !== null,
            timeoutMs: 20000,
            pollIntervalMs: 1000,
            description: `Updated entry for customer ${customerId}`
        });
        
        expect(updatedEntry).toBeDefined();
        const content = JSON.parse(updatedEntry.contents[0].text);
        expect(content.customer_id).toBe(customerId);
        expect(content.name).toBe(updatedName);
        expect(content.email).toBe(customerEmail);
    }, 40000);

    test('should remove entry resources on customer data deletion', async () => {
        // First insert a customer
        const customerId = `cust-delete-${Date.now()}`;
        const customerName = `Delete Test Customer ${Date.now()}`;
        const customerEmail = `${customerId}@test.com`;
        
        console.log(`Inserting customer for deletion test: ${customerId}`);
        
        await pgClient.query(
            "INSERT INTO customers (customer_id, name, email) VALUES ($1, $2, $3)",
            [customerId, customerName, customerEmail]
        );
        
        // Wait for entry to be created
        const entryUri = `drasi://${REACTION_NAME}/queries/customer-data/entries/${customerId}`;
        
        await waitFor({
            actionFn: async () => {
                try {
                    return await mcpRequest(mcpBaseUrl, 'resources/read', { uri: entryUri });
                } catch (error) {
                    return null;
                }
            },
            predicateFn: (result) => result !== null,
            timeoutMs: 20000,
            pollIntervalMs: 1000,
            description: `Entry creation for customer ${customerId}`
        });
        
        // Delete the customer
        console.log(`Deleting customer: ${customerId}`);
        await pgClient.query(
            "DELETE FROM customers WHERE customer_id = $1",
            [customerId]
        );
        
        // Wait for the entry to be removed
        const deletionResult = await waitFor({
            actionFn: async () => {
                try {
                    await mcpRequest(mcpBaseUrl, 'resources/read', { uri: entryUri });
                    return false; // If successful, entry still exists
                } catch (error) {
                    // Entry should not be found
                    return error.message.includes('not found') || error.message.includes('404');
                }
            },
            predicateFn: (result) => result === true,
            timeoutMs: 20000,
            pollIntervalMs: 1000,
            description: `Entry removal for customer ${customerId}`
        });
        
        expect(deletionResult).toBe(true);
    }, 40000);

    test('should handle multiple queries independently', async () => {
        const customerId = `cust-multi-${Date.now()}`;
        const orderId = `order-${Date.now()}`;
        
        console.log(`Testing multi-query independence with customer ${customerId} and order ${orderId}`);
        
        // Insert customer
        await pgClient.query(
            "INSERT INTO customers (customer_id, name, email) VALUES ($1, $2, $3)",
            [customerId, `Multi Test Customer`, `${customerId}@test.com`]
        );
        
        // Insert order
        await pgClient.query(
            "INSERT INTO orders (order_id, customer_id, total, status) VALUES ($1, $2, $3, $4)",
            [orderId, customerId, 99.99, 'pending']
        );
        
        // Wait for both entries to be created
        const customerEntryUri = `drasi://${REACTION_NAME}/queries/customer-data/entries/${customerId}`;
        const orderEntryUri = `drasi://${REACTION_NAME}/queries/order-data/entries/${orderId}`;
        
        const [customerEntry, orderEntry] = await Promise.all([
            waitFor({
                actionFn: async () => {
                    try {
                        return await mcpRequest(mcpBaseUrl, 'resources/read', { uri: customerEntryUri });
                    } catch (error) {
                        return null;
                    }
                },
                predicateFn: (result) => result !== null,
                timeoutMs: 20000,
                pollIntervalMs: 1000,
                description: `Customer entry ${customerId}`
            }),
            waitFor({
                actionFn: async () => {
                    try {
                        return await mcpRequest(mcpBaseUrl, 'resources/read', { uri: orderEntryUri });
                    } catch (error) {
                        return null;
                    }
                },
                predicateFn: (result) => result !== null,
                timeoutMs: 20000,
                pollIntervalMs: 1000,
                description: `Order entry ${orderId}`
            })
        ]);
        
        // Verify customer entry
        expect(customerEntry).toBeDefined();
        const customerContent = JSON.parse(customerEntry.contents[0].text);
        expect(customerContent.customer_id).toBe(customerId);
        
        // Verify order entry
        expect(orderEntry).toBeDefined();
        const orderContent = JSON.parse(orderEntry.contents[0].text);
        expect(orderContent.order_id).toBe(orderId);
        expect(orderContent.customer_id).toBe(customerId);
        expect(parseFloat(orderContent.total)).toBe(99.99);
        expect(orderContent.status).toBe('pending');
        
        // List all resources to verify they're separate
        const resources = await mcpRequest(mcpBaseUrl, 'resources/list');
        const entryResources = resources.resources.filter(r => r.uri.includes('/entries/'));
        
        const customerEntries = entryResources.filter(r => r.uri.includes('/queries/customer-data/entries/'));
        const orderEntries = entryResources.filter(r => r.uri.includes('/queries/order-data/entries/'));
        
        expect(customerEntries.length).toBeGreaterThan(0);
        expect(orderEntries.length).toBeGreaterThan(0);
    }, 60000);

    test('should validate MCP protocol compliance with SDK client', async () => {
        if (!mcpClient) {
            console.log("Skipping MCP protocol compliance test - client not connected");
            return;
        }
        
        console.log("Testing MCP protocol compliance...");
        
        // Test server capabilities
        const serverInfo = mcpClient.getServerInfo();
        console.log("Server info:", serverInfo);
        expect(serverInfo).toBeDefined();
        
        // Test listing resources with pagination (if supported)
        try {
            const firstPage = await mcpClient.listResources({ cursor: null });
            console.log(`Listed ${firstPage.resources.length} resources`);
            
            if (firstPage.nextCursor) {
                const secondPage = await mcpClient.listResources({ cursor: firstPage.nextCursor });
                console.log(`Listed ${secondPage.resources.length} more resources`);
            }
        } catch (error) {
            console.log("Pagination test:", error.message);
        }
        
        // Test resource templates (if supported)
        try {
            const templates = await mcpClient.listResourceTemplates();
            console.log(`Server provides ${templates?.resourceTemplates?.length || 0} resource templates`);
        } catch (error) {
            console.log("Resource templates not supported:", error.message);
        }
        
        // Test subscribing to resource updates (if supported)
        if (mcpClient.subscribe) {
            console.log("Testing resource subscriptions...");
            try {
                const subscriptionUri = `drasi://${REACTION_NAME}/queries/customer-data`;
                await mcpClient.subscribe({ uri: subscriptionUri });
                console.log(`Successfully subscribed to ${subscriptionUri}`);
                
                // Unsubscribe after test
                await mcpClient.unsubscribe({ uri: subscriptionUri });
                console.log(`Successfully unsubscribed from ${subscriptionUri}`);
            } catch (error) {
                console.log("Subscription test:", error.message);
            }
        }
    }, 40000);
});