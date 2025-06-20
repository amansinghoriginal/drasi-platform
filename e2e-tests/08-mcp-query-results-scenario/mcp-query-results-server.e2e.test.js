const { describe, beforeAll, afterAll, test, expect } = require('@jest/globals');
const { Client: PgClient } = require('pg');
const path = require('path');
const fs = require('fs');
const yaml = require('js-yaml');
const { Client } = require('@modelcontextprotocol/sdk/client/index.js');
const { StreamableHTTPClientTransport } = require('@modelcontextprotocol/sdk/client/streamableHttp.js');
const { ResourceUpdatedNotificationSchema } = require('@modelcontextprotocol/sdk/types.js');

const PortForward = require('../fixtures/port-forward');
const deployResources = require('../fixtures/deploy-resources');
const deleteResources = require('../fixtures/delete-resources');
const { waitFor } = require('../fixtures/infrastructure');

const SCENARIO_DIR = __dirname;
const K8S_RESOURCES_FILE = path.join(SCENARIO_DIR, 'k8s-mcp-test-resources.yaml');
const DRASI_SOURCE_FILE = path.join(SCENARIO_DIR, 'drasi-mcp-source.yaml');
const DRASI_QUERY_FILE = path.join(SCENARIO_DIR, 'drasi-mcp-query.yaml');
const DRASI_REACTION_PROVIDER_FILE = path.join(SCENARIO_DIR, 'drasi-mcp-reaction-provider.yaml');
const DRASI_REACTION_FILE = path.join(SCENARIO_DIR, 'drasi-mcp-reaction.yaml');


const QUERY_NAME = 'mcp-product-updates';

function loadYaml(filePath) {
    const content = fs.readFileSync(filePath, 'utf8');
    return yaml.loadAll(content);
}

describe('McpQueryResultsServer E2E Tests', () => {
    let pgClient;
    let pgPortForward;
    let mcpServerPortForward;
    let mcpSdkClient;

    const pgConnectionConfig = {
        user: 'testuser',
        password: 'testpassword',
        database: 'mcp_test_db',
        host: 'localhost',
        port: 0 // Will be set by port-forward
    };

    // Load all YAML resources once
    const k8sResources = loadYaml(K8S_RESOURCES_FILE);
    const sourceResources = loadYaml(DRASI_SOURCE_FILE);
    const queryResources = loadYaml(DRASI_QUERY_FILE);
    const reactionProviderResources = loadYaml(DRASI_REACTION_PROVIDER_FILE);
    const reactionResources = loadYaml(DRASI_REACTION_FILE);

    const allTestResources = [
        ...k8sResources,
        ...sourceResources,
        ...queryResources,
        ...reactionProviderResources,
        ...reactionResources
    ];

    beforeAll(async () => {
        console.log("Starting E2E test setup for MCP Query Results Server...");
        
        // 1. Deploy K8s resources
        console.log('Deploying K8s resources...');
        await deployResources(k8sResources);

        // 2. Wait for K8s resources to stabilize
        console.log('Waiting for K8s resources to stabilize...');
        await waitFor({ 
            timeoutMs: 30000, 
            description: 'K8s resources to stabilize' 
        });

        // 3. Set up PostgreSQL port forward
        pgPortForward = new PortForward('mcp-test-db', 5432);
        pgConnectionConfig.port = await pgPortForward.start();
        
        pgClient = new PgClient(pgConnectionConfig);
        await pgClient.connect();
        console.log('Connected to PostgreSQL via port-forward.');

        // Clear ALL existing data to ensure clean state
        console.log('Clearing all product data...');
        await pgClient.query(`DELETE FROM products`);
        console.log('All product data cleared.');
        
        // Wait for deletion to propagate through Drasi
        await new Promise(r => setTimeout(r, 3000));

        // 4. Wait for Drasi API to be accessible
        console.log('Checking Drasi API accessibility...');
        await waitFor({
            actionFn: async () => {
                try {
                    const cp = require('child_process');
                    cp.execSync('drasi list source', { encoding: 'utf-8' });
                    return true;
                } catch (error) {
                    console.log('Drasi API not ready yet, waiting...');
                    return false;
                }
            },
            predicateFn: (result) => result,
            description: 'Drasi API to be accessible',
            timeoutMs: 120000,
            pollIntervalMs: 5000
        });
        console.log('Drasi API is accessible.');

        // 5. Deploy Drasi Source
        console.log('Deploying Drasi Source resources...');
        await deployResources(sourceResources);

        // 6. Deploy Drasi Query
        console.log('Deploying Drasi Query resources...');
        await deployResources(queryResources);

        // 7. Deploy Reaction Provider
        console.log('Deploying Drasi ReactionProvider resources...');
        await deployResources(reactionProviderResources);

        // 8. Deploy Reaction
        console.log('Deploying Drasi Reaction resources...');
        await deployResources(reactionResources);

        // 9. Wait for system to stabilize
        console.log('Waiting for system to stabilize...');
        await waitFor({
            timeoutMs: 30000,
            description: 'system to stabilize'
        });

        // 10. Set up MCP server port forward
        const mcpServiceName = `mcp-server-e2e-test-mcp-server`; // {reaction-name}-{endpoint-name}
        mcpServerPortForward = new PortForward(mcpServiceName, 8080, 'drasi-system');
        const mcpServerActualPort = await mcpServerPortForward.start();
        console.log(`MCP Server port-forwarded to local port ${mcpServerActualPort}`);

        // 11. Connect MCP client
        const mcpServerUrl = new URL(`http://localhost:${mcpServerActualPort}/mcp`);
        const transport = new StreamableHTTPClientTransport(mcpServerUrl);

        mcpSdkClient = new Client({
            name: "e2e-test-client",
            version: "1.0.0"
        });

        await mcpSdkClient.connect(transport);
        console.log('MCP SDK Client connected.');

    }, 300000);

    afterAll(async () => {
        console.log("Cleaning up E2E test resources...");
        
        if (mcpSdkClient) {
            try { await mcpSdkClient.close(); console.log("MCP SDK client closed."); } catch (e) { console.warn("Error closing MCP SDK client:", e.message); }
        }
        if (mcpServerPortForward) {
            try { await mcpServerPortForward.stop(); console.log("MCP server port-forward stopped."); } catch (e) { console.warn("Error stopping MCP server port-forward:", e.message); }
        }
        if (pgClient) {
            try { await pgClient.end(); console.log("PostgreSQL client disconnected."); } catch (e) { console.warn("Error disconnecting PostgreSQL client:", e.message); }
        }
        if (pgPortForward) {
            try { await pgPortForward.stop(); console.log("PostgreSQL port-forward stopped."); } catch (e) { console.warn("Error stopping PostgreSQL port-forward:", e.message); }
        }

        console.log('Deleting all test resources...');
        // await deleteResources(allTestResources); 
        
        console.log('Cleanup complete.');
    }, 180000);

    beforeEach(async () => {
        // Clean up ALL test data before each test for complete isolation
        await pgClient.query(`DELETE FROM products`);
        // Wait for changes to propagate through Drasi
        await new Promise(r => setTimeout(r, 2000));
    });

    afterEach(async () => {
        // Clean up ALL test data after each test
        await pgClient.query(`DELETE FROM products`);
        // Unsubscribe from any active subscriptions to avoid interference
        try {
            await mcpSdkClient.unsubscribeResource({ uri: `drasi://queries/${QUERY_NAME}` });
        } catch (e) {
            // Ignore if not subscribed
        }
    });

    const waitForMcpNotification = (
        methodName,
        predicate,
        timeout = 20000
    ) => {
        return new Promise((resolve, reject) => {
            let timeoutId;
            
            // Set up the notification handler
            const handler = async (notification) => {
                if (predicate(notification.params)) {
                    clearTimeout(timeoutId);
                    resolve(notification.params);
                }
            };
            
            // Correct API usage - pass the schema and handler
            mcpSdkClient.setNotificationHandler(
                ResourceUpdatedNotificationSchema,
                handler
            );

            timeoutId = setTimeout(() => {
                reject(new Error(`Timeout waiting for MCP notification "${methodName}" after ${timeout / 1000}s`));
            }, timeout);
        });
    };


    test('MCP server should list the configured query as a static resource', async () => {
        // Check static resources - queries should now be listed here
        const resourcesResult = await mcpSdkClient.listResources();
        expect(resourcesResult.resources).toBeDefined();
        expect(resourcesResult.resources.length).toBeGreaterThan(0);
        
        // Check for our specific query as a static resource
        const queryResource = resourcesResult.resources.find(r => 
            r.uri === `drasi://queries/${QUERY_NAME}`
        );
        expect(queryResource).toBeDefined();
        expect(queryResource.name).toBe(QUERY_NAME);
        expect(queryResource.description).toBe("Live product information for MCP E2E tests");
        expect(queryResource.mimeType).toBe("application/json");
        
        // Check resource templates - only entry template should be listed
        const templatesResult = await mcpSdkClient.listResourceTemplates();
        expect(templatesResult.resourceTemplates).toBeDefined();
        
        // Should NOT have query template anymore
        const queryTemplate = templatesResult.resourceTemplates.find(t => 
            t.uriTemplate === "drasi://queries/{queryId}"
        );
        expect(queryTemplate).toBeUndefined();
        
        // Should still have entry template
        const entryTemplate = templatesResult.resourceTemplates.find(t =>
            t.uriTemplate === "drasi://entries/{queryId}/{entryKey}"
        );
        expect(entryTemplate).toBeDefined();
        expect(entryTemplate.name).toBe("Dataset Entry");
    });

    test('MCP server should return initial product entries', async () => {
        // ARRANGE
        const testProductId1 = 'TEST_001';
        const testProductId2 = 'TEST_002';
        await pgClient.query(`
            INSERT INTO products (product_id, name, description, price, last_updated) 
            VALUES 
                ($1, 'Test Laptop', 'Test laptop description', 999.99, CURRENT_TIMESTAMP),
                ($2, 'Test Mouse', 'Test mouse description', 29.99, CURRENT_TIMESTAMP)
        `, [testProductId1, testProductId2]);
        
        // Wait for data to propagate through Drasi
        await new Promise(r => setTimeout(r, 5000));
        
        const queryResourceUri = `drasi://queries/${QUERY_NAME}`;

        // ACT
        const queryResource = await mcpSdkClient.readResource({ uri: queryResourceUri });
        
        // ASSERT
        expect(queryResource.contents).toBeDefined();
        expect(queryResource.contents.length).toBeGreaterThan(0);
        
        const queryContent = queryResource.contents[0];
        expect(queryContent.text).toBeDefined();
        const queryData = JSON.parse(queryContent.text);
        expect(queryData.entries).toBeInstanceOf(Array);
        expect(queryData.entries.length).toBe(2);

        const entryUri1 = queryData.entries.find((e) => e.endsWith(`/${testProductId1}`));
        expect(entryUri1).toBeDefined();
        
        const entryResource = await mcpSdkClient.readResource({ uri: entryUri1 });
        expect(entryResource.contents.length).toBeGreaterThan(0);
        const entryData = JSON.parse(entryResource.contents[0].text);
        expect(entryData.id).toBe(testProductId1);
        expect(entryData.product_name).toBe('Test Laptop');
    });

    test('MCP server should reflect new product insertion with notification', async () => {
        // ARRANGE
        const testProductId1 = 'TEST_001';
        const testProductId2 = 'TEST_002';
        const newProductId = 'TEST_003';
        
        // Insert initial products
        await pgClient.query(`
            INSERT INTO products (product_id, name, description, price, last_updated) 
            VALUES 
                ($1, 'Initial Product 1', 'Description 1', 100.00, CURRENT_TIMESTAMP),
                ($2, 'Initial Product 2', 'Description 2', 200.00, CURRENT_TIMESTAMP)
        `, [testProductId1, testProductId2]);
        
        await new Promise(r => setTimeout(r, 2000));
        
        const queryResourceUri = `drasi://queries/${QUERY_NAME}`;

        // Set up notification listener
        const notificationPromise = waitForMcpNotification(
            'notifications/resources/updated',
            (params) => params.uri === queryResourceUri
        );

        // ACT
        await pgClient.query(
            "INSERT INTO products (product_id, name, description, price) VALUES ($1, 'Gaming Keyboard', 'Mechanical RGB Keyboard', 75.00)",
            [newProductId]
        );

        // ASSERT
        const notification = await notificationPromise;
        expect(notification).toBeDefined();
        expect(notification.uri).toBe(queryResourceUri);

        // Wait for data to propagate
        await new Promise(r => setTimeout(r, 2000));

        const queryResource = await mcpSdkClient.readResource({ uri: queryResourceUri });
        const queryData = JSON.parse(queryResource.contents[0].text);
        expect(queryData.entries.length).toBe(3);
        
        const newEntryUri = queryData.entries.find((e) => e.endsWith(`/${newProductId}`));
        expect(newEntryUri).toBeDefined();

        const entryResource = await mcpSdkClient.readResource({ uri: newEntryUri });
        const entryData = JSON.parse(entryResource.contents[0].text);
        expect(entryData.id).toBe(newProductId);
        expect(entryData.product_name).toBe('Gaming Keyboard');
    });

    test('MCP server should reflect product update with notification', async () => {
        // ARRANGE
        const productIdToUpdate = 'TEST_001';
        
        // Insert initial product
        await pgClient.query(
            "INSERT INTO products (product_id, name, description, price, last_updated) VALUES ($1, $2, $3, $4, CURRENT_TIMESTAMP)",
            [productIdToUpdate, 'Original Laptop', 'Original description', 999.99]
        );
        
        await new Promise(r => setTimeout(r, 2000));
        
        const entryResourceUri = `drasi://entries/${QUERY_NAME}/${productIdToUpdate}`;

        // Set up notification listener
        const notificationPromise = waitForMcpNotification(
            'notifications/resources/updated',
            (params) => params.uri === entryResourceUri
        );
        
        // ACT
        await pgClient.query(
            "UPDATE products SET price = $1, name = $2, last_updated = CURRENT_TIMESTAMP WHERE product_id = $3",
            [1150.00, 'Updated Laptop', productIdToUpdate]
        );
        
        // ASSERT
        const notification = await notificationPromise;
        expect(notification).toBeDefined();
        expect(notification.uri).toBe(entryResourceUri);

        // Wait for data to propagate
        await new Promise(r => setTimeout(r, 2000));

        const updatedEntry = await mcpSdkClient.readResource({ uri: entryResourceUri });
        const updatedData = JSON.parse(updatedEntry.contents[0].text);
        expect(updatedData.id).toBe(productIdToUpdate);
        expect(updatedData.product_name).toBe('Updated Laptop');
        expect(parseFloat(updatedData.current_price)).toBe(1150.00);
    });

    test('MCP server should reflect product deletion with notification', async () => {
        // ARRANGE
        const productIdToDelete = 'TEST_002';
        const productIdToKeep = 'TEST_001';
        
        // Insert initial products
        await pgClient.query(`
            INSERT INTO products (product_id, name, description, price, last_updated) 
            VALUES 
                ($1, 'Product to Keep', 'Description', 100.00, CURRENT_TIMESTAMP),
                ($2, 'Product to Delete', 'Description', 200.00, CURRENT_TIMESTAMP)
        `, [productIdToKeep, productIdToDelete]);
        
        await new Promise(r => setTimeout(r, 2000));
        
        const queryResourceUri = `drasi://queries/${QUERY_NAME}`;

        // Set up notification listener
        const notificationPromise = waitForMcpNotification(
            'notifications/resources/updated',
            (params) => params.uri === queryResourceUri
        );

        // ACT
        await pgClient.query("DELETE FROM products WHERE product_id = $1", [productIdToDelete]);

        // ASSERT
        const notification = await notificationPromise;
        expect(notification).toBeDefined();
        expect(notification.uri).toBe(queryResourceUri);
        
        // Wait for data to propagate
        await new Promise(r => setTimeout(r, 2000));

        const queryResource = await mcpSdkClient.readResource({ uri: queryResourceUri });
        const queryData = JSON.parse(queryResource.contents[0].text);
        expect(queryData.entries.length).toBe(1); // Only productIdToKeep remains
        
        const deletedEntryUri = queryData.entries.find((e) => e.endsWith(`/${productIdToDelete}`));
        expect(deletedEntryUri).toBeUndefined();
        
        const keptEntryUri = queryData.entries.find((e) => e.endsWith(`/${productIdToKeep}`));
        expect(keptEntryUri).toBeDefined();
    });

    test('MCP server should support resource subscriptions', async () => {
        // ARRANGE
        const newProductId = 'TEST_004';
        const queryResourceUri = `drasi://queries/${QUERY_NAME}`;
        
        // Subscribe to the query resource
        await mcpSdkClient.subscribeResource({ uri: queryResourceUri });
        
        // Set up notification listener before making changes
        const notificationPromise = waitForMcpNotification(
            'notifications/resources/updated',
            (params) => params.uri === queryResourceUri
        );

        // ACT
        await pgClient.query(
            "INSERT INTO products (product_id, name, description, price) VALUES ($1, $2, $3, $4)",
            [newProductId, 'USB Hub', '7-port USB 3.0 Hub', 35.00]
        );

        // ASSERT
        const notification = await notificationPromise;
        expect(notification).toBeDefined();
        expect(notification.uri).toBe(queryResourceUri);

        // Wait for data to propagate
        await new Promise(r => setTimeout(r, 2000));

        // Verify the new product is in the results
        const queryResource = await mcpSdkClient.readResource({ uri: queryResourceUri });
        const queryData = JSON.parse(queryResource.contents[0].text);
        const newProductUri = queryData.entries.find((e) => e.endsWith(`/${newProductId}`));
        expect(newProductUri).toBeDefined();

        // Unsubscribe
        await mcpSdkClient.unsubscribeResource({ uri: queryResourceUri });
    });

    test('MCP server static resources can be discovered without prior knowledge', async () => {
        // Test that clients can discover all available queries without knowing query IDs
        const resourcesResult = await mcpSdkClient.listResources();
        
        // Find all query resources
        const queryResources = resourcesResult.resources.filter(r => 
            r.uri.startsWith('drasi://queries/')
        );
        
        // We should have at least our test query
        expect(queryResources.length).toBeGreaterThanOrEqual(1);
        
        // Each query resource should have proper metadata
        queryResources.forEach(resource => {
            expect(resource.name).toBeTruthy(); // Name is just the query ID now
            expect(resource.mimeType).toBe('application/json');
            // Description can be optional, but if present should be a string
            if (resource.description) {
                expect(typeof resource.description).toBe('string');
            }
        });
    });

    test('MCP server should expose resources and tools capabilities', async () => {
        // Get server capabilities from initialization
        const serverInfo = mcpSdkClient._serverCapabilities;
        
        // Verify we expose resources and tools capabilities
        expect(serverInfo).toBeDefined();
        expect(serverInfo.resources).toBeDefined();
        expect(serverInfo.tools).toBeDefined();
        expect(serverInfo.prompts).toBeDefined(); // Server returns empty object {} for prompts
        expect(serverInfo.sampling).toBeUndefined();
        
        // List tools - should now return our query tool
        const toolsResult = await mcpSdkClient.listTools();
        expect(toolsResult.tools).toBeDefined();
        expect(toolsResult.tools.length).toBeGreaterThan(0);
        
        // Check for our specific query tool
        const queryTool = toolsResult.tools.find(t => 
            t.name === `get_${QUERY_NAME}_results`
        );
        expect(queryTool).toBeDefined();
        expect(queryTool.description).toBe("Live product information for MCP E2E tests");
        expect(queryTool.inputSchema).toBeDefined();
        expect(queryTool.inputSchema.type).toBe("object");
        
        // List prompts - should return empty list
        const promptsResult = await mcpSdkClient.listPrompts();
        expect(promptsResult.prompts).toBeDefined();
        expect(promptsResult.prompts.length).toBe(0);
    });

    test('MCP tools should fetch live query results with filtering', async () => {
        // ARRANGE
        const testProductId1 = 'TEST_001';
        const testProductId2 = 'TEST_002';
        const testProductId3 = 'TEST_003';
        
        // Insert test products with different prices
        await pgClient.query(`
            INSERT INTO products (product_id, name, description, price, last_updated) 
            VALUES 
                ($1, 'Budget Laptop', 'Affordable laptop', 599.99, CURRENT_TIMESTAMP),
                ($2, 'Premium Laptop', 'High-end laptop', 1999.99, CURRENT_TIMESTAMP),
                ($3, 'Mid-range Laptop', 'Standard laptop', 999.99, CURRENT_TIMESTAMP)
        `, [testProductId1, testProductId2, testProductId3]);
        
        // Wait for data to propagate through Drasi
        await new Promise(r => setTimeout(r, 5000));
        
        const toolName = `get_${QUERY_NAME}_results`;
        
        // ACT & ASSERT
        
        // Test 1: Call tool without parameters (get all results)
        const allResults = await mcpSdkClient.callTool({
            name: toolName,
            arguments: {}
        });
        
        expect(allResults.content).toBeDefined();
        expect(allResults.content.length).toBeGreaterThan(0);
        const allData = JSON.parse(allResults.content[0].text);
        expect(allData.queryId).toBe(QUERY_NAME);
        expect(allData.description).toBe("Live product information for MCP E2E tests");
        expect(allData.resultCount).toBe(3);
        expect(allData.results.length).toBe(3);
        
        // Test 2: Call tool with limit parameter
        const limitedResults = await mcpSdkClient.callTool({
            name: toolName,
            arguments: { limit: 2 }
        });
        
        const limitedData = JSON.parse(limitedResults.content[0].text);
        expect(limitedData.resultCount).toBe(2);
        expect(limitedData.totalCount).toBe(3);
        expect(limitedData.results.length).toBe(2);
        
        // Test 3: Call tool with filter parameter
        // Note: This tests the filter logic but may need adjustment based on actual data structure
        const filteredResults = await mcpSdkClient.callTool({
            name: toolName,
            arguments: { 
                filter: { 
                    product_name: 'Premium Laptop' 
                } 
            }
        });
        
        const filteredData = JSON.parse(filteredResults.content[0].text);
        expect(filteredData.results.length).toBe(1);
        expect(filteredData.results[0].product_name).toBe('Premium Laptop');
        expect(filteredData.results[0].id).toBe(testProductId2);
    });

    test('MCP server should handle subscription persistence across rapid changes', async () => {
        // ARRANGE
        const testProductId1 = 'TEST_001';
        const testProductId2 = 'TEST_002';
        const testProductId3 = 'TEST_003';
        const testProductId5 = 'TEST_005';
        
        // Insert initial products
        await pgClient.query(`
            INSERT INTO products (product_id, name, description, price, last_updated) 
            VALUES 
                ($1, 'Initial Product 1', 'Description', 100.00, CURRENT_TIMESTAMP),
                ($2, 'Initial Product 2', 'Description', 200.00, CURRENT_TIMESTAMP),
                ($3, 'Initial Product 3', 'Description', 300.00, CURRENT_TIMESTAMP)
        `, [testProductId1, testProductId2, testProductId3]);
        
        await new Promise(r => setTimeout(r, 2000));
        
        const queryResourceUri = `drasi://queries/${QUERY_NAME}`;
        const entryResourceUri = `drasi://entries/${QUERY_NAME}/${testProductId1}`;
        
        // Subscribe to both query and entry resources
        await mcpSdkClient.subscribeResource({ uri: queryResourceUri });
        await mcpSdkClient.subscribeResource({ uri: entryResourceUri });
        
        // Track all notifications received
        const notifications = [];
        mcpSdkClient.setNotificationHandler(
            ResourceUpdatedNotificationSchema,
            (notification) => {
                notifications.push({
                    uri: notification.params.uri,
                    timestamp: Date.now()
                });
            }
        );
        
        // ACT - Make rapid successive changes
        await pgClient.query(
            "UPDATE products SET price = $1 WHERE product_id = $2",
            [1200.00, testProductId1]
        );
        
        await pgClient.query(
            "INSERT INTO products (product_id, name, description, price) VALUES ($1, $2, $3, $4)",
            [testProductId5, 'Monitor Stand', 'Adjustable monitor stand', 45.00]
        );
        
        await pgClient.query(
            "UPDATE products SET price = $1 WHERE product_id = $2",
            [1250.00, testProductId1]
        );
        
        await pgClient.query(
            "DELETE FROM products WHERE product_id = $1",
            [testProductId3]
        );
        
        // Wait for notifications to arrive
        await new Promise(r => setTimeout(r, 3000));
        
        // ASSERT
        const queryNotifications = notifications.filter(n => n.uri === queryResourceUri);
        const entryNotifications = notifications.filter(n => n.uri === entryResourceUri);
        
        // Should have query notifications for INSERT and DELETE
        expect(queryNotifications.length).toBeGreaterThanOrEqual(2);
        
        // Should have entry notifications for the two UPDATEs on testProductId1
        expect(entryNotifications.length).toBeGreaterThanOrEqual(2);
        
        // Verify the current state is correct
        const queryResource = await mcpSdkClient.readResource({ uri: queryResourceUri });
        const queryData = JSON.parse(queryResource.contents[0].text);
        
        // Should have testProductId1, testProductId2, testProductId5 (testProductId3 deleted)
        expect(queryData.entries.length).toBe(3);
        expect(queryData.entries.find(e => e.endsWith(`/${testProductId3}`))).toBeUndefined();
        expect(queryData.entries.find(e => e.endsWith(`/${testProductId5}`))).toBeDefined();
        
        // Verify testProductId1 has the latest price
        const entryResource = await mcpSdkClient.readResource({ uri: entryResourceUri });
        const entryData = JSON.parse(entryResource.contents[0].text);
        expect(parseFloat(entryData.current_price)).toBe(1250.00);
        
        // Unsubscribe
        await mcpSdkClient.unsubscribeResource({ uri: queryResourceUri });
        await mcpSdkClient.unsubscribeResource({ uri: entryResourceUri });
    });
});