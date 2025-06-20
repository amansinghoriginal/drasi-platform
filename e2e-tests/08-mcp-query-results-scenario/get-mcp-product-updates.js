// Fetches and prints the latest product data from Drasi MCP server
const { Client } = require('@modelcontextprotocol/sdk/client');
const { StreamableHTTPClientTransport } = require('@modelcontextprotocol/sdk/client/streamableHttp');

async function main() {
  const mcpUrl = 'http://localhost:8080/mcp';
  const transport = new StreamableHTTPClientTransport(mcpUrl);
  const client = new Client({ name: 'product-fetcher', version: '1.0.0' });

  await client.connect(transport);

  const resourceUri = 'drasi://queries/mcp-product-updates';
  const resource = await client.readResource({ uri: resourceUri });

  if (resource.contents && resource.contents.length > 0) {
    const data = JSON.parse(resource.contents[0].text);
    console.log('Latest products:', data.entries);
  } else {
    console.log('No product data found.');
  }

  await client.close();
}

main().catch(console.error);
