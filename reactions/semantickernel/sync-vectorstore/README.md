# Sync Semantic Kernel Vector Store Reaction

This reaction synchronizes the results of Drasi queries with Semantic Kernel vector stores, enabling real-time vector search capabilities on continuously updated query results.

## Features

- **Real-time Vector Sync**: Automatically processes incremental changes from Drasi queries and maintains synchronized vector stores
- **Multiple Vector Store Support**: Works with Qdrant, Weaviate, Azure AI Search, Redis, Pinecone, Chroma, and in-memory stores via Semantic Kernel abstractions  
- **Multiple Embedding Services**: Supports OpenAI, Azure OpenAI, Ollama, and Hugging Face embedding services
- **Flexible Document Processing**: Configurable document templates and metadata extraction
- **Batch Processing**: Efficient bulk operations for high-throughput scenarios
- **Error Recovery**: Built-in retry logic and graceful error handling

## Quick Start

### 1. Deploy the Reaction Provider

First, ensure the reaction provider is registered:

```bash
drasi apply -f reaction-provider.yaml
```

### 2. Create Secrets for External Services

Create Kubernetes secrets for your vector store and embedding service credentials:

```bash
# For Qdrant vector store
kubectl create secret generic qdrant-creds \
  --from-literal=connection-string="Endpoint=https://your-qdrant-cluster.qdrant.io;ApiKey=your-api-key"

# For Azure OpenAI embedding service
kubectl create secret generic azure-openai-creds \
  --from-literal=api-key="your-azure-openai-api-key"
```

### 3. Configure and Deploy a Reaction

Create a reaction configuration file:

```yaml
apiVersion: v1
kind: Reaction
name: product-vector-sync
spec:
  kind: SyncSemanticKernelVectorStore
  properties:
    vectorStoreType: "Qdrant"
    connectionString:
      kind: Secret
      name: qdrant-creds
      key: connection-string
    embeddingServiceType: "AzureOpenAI"
    embeddingEndpoint: "https://your-org.openai.azure.com"
    embeddingApiKey:
      kind: Secret
      name: azure-openai-creds
      key: api-key
    embeddingModel: "text-embedding-3-small"
    embeddingDimensions: 1536
  queries:
    product-catalog: |
      {
        "collectionName": "products",
        "keyField": "product_id",
        "documentTemplate": "Product: {name}\nDescription: {description}\nCategory: {category}",
        "metadataFields": ["category", "price", "brand"],
        "createCollection": true
      }
```

Deploy the reaction:

```bash
drasi apply -f your-reaction.yaml
```

## Configuration Reference

### Global Properties

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `vectorStoreType` | string | Yes | - | Type of vector store (Qdrant, Weaviate, AzureAISearch, Redis, Pinecone, Chroma, InMemory) |
| `connectionString` | string | Yes | - | Connection string for the vector store |
| `embeddingServiceType` | string | Yes | - | Type of embedding service (OpenAI, AzureOpenAI, OllamaEmbedding, HuggingFaceEmbedding) |
| `embeddingEndpoint` | string | No | - | Endpoint URL for the embedding service |
| `embeddingApiKey` | string | No | - | API key for the embedding service |
| `embeddingModel` | string | No | text-embedding-3-small | Model name/ID to use for embeddings |
| `embeddingDimensions` | integer | No | 1536 | Number of dimensions for embeddings |
| `batchSize` | integer | No | 100 | Batch size for bulk operations |
| `maxRetries` | integer | No | 3 | Maximum number of retries for failed operations |
| `retryDelayMs` | integer | No | 1000 | Delay in milliseconds between retries |

### Per-Query Configuration

Each query can have its own JSON configuration:

```json
{
  "collectionName": "my_collection",
  "keyField": "id",
  "documentTemplate": "Title: {title}\nContent: {content}",
  "metadataFields": ["category", "date", "author"],
  "vectorField": "content_vector",
  "createCollection": true,
  "collectionConfig": {
    "dimensions": 1536,
    "distance": "Cosine"
  }
}
```

| Property | Type | Required | Default | Description |
|----------|------|----------|---------|-------------|
| `collectionName` | string | Yes | - | Name of the vector collection to sync with |
| `keyField` | string | Yes | - | Field in query results to use as unique identifier |
| `documentTemplate` | string | Yes | - | Template for generating document text from query results |
| `metadataFields` | array | No | [] | List of fields to extract as metadata |
| `vectorField` | string | No | content_vector | Field name for the vector in the document |
| `createCollection` | boolean | No | true | Whether to create collection if it doesn't exist |
| `collectionConfig` | object | No | - | Configuration for new collections |

#### Document Template Format

Use `{fieldName}` placeholders in templates:

```
"Product: {name}\nDescription: {description}\nPrice: ${price}\nCategory: {category}"
```

## Vector Store Connection Strings

### Qdrant
```
Endpoint=https://your-cluster.qdrant.io;ApiKey=your-api-key
```

### Weaviate  
```
Endpoint=http://localhost:8080;ApiKey=your-api-key
```

### Azure AI Search
```
Endpoint=https://your-service.search.windows.net;ApiKey=your-api-key
```

### Redis
```
localhost:6379
```

### Pinecone
```
ApiKey=your-api-key;Environment=us-west1-gcp
```

## Embedding Service Configuration

### OpenAI
```yaml
embeddingServiceType: "OpenAI"
embeddingApiKey: "your-openai-api-key"
embeddingModel: "text-embedding-3-small"
```

### Azure OpenAI
```yaml
embeddingServiceType: "AzureOpenAI"
embeddingEndpoint: "https://your-org.openai.azure.com"
embeddingApiKey: "your-azure-openai-api-key"
embeddingModel: "text-embedding-3-small"
```

### Ollama
```yaml
embeddingServiceType: "OllamaEmbedding"
embeddingEndpoint: "http://localhost:11434"
embeddingModel: "nomic-embed-text"
```

### Hugging Face
```yaml
embeddingServiceType: "HuggingFaceEmbedding"
embeddingEndpoint: "https://api-inference.huggingface.co"
embeddingApiKey: "your-hf-token"
embeddingModel: "sentence-transformers/all-MiniLM-L6-v2"
```

## Example Use Cases

### 1. E-commerce Product Search

Sync product catalog data to enable semantic search:

```yaml
queries:
  product-catalog: |
    {
      "collectionName": "products",
      "keyField": "product_id",
      "documentTemplate": "Product: {name}\nDescription: {description}\nBrand: {brand}\nCategory: {category}",
      "metadataFields": ["category", "brand", "price", "in_stock"],
      "createCollection": true
    }
```

### 2. Customer Support Knowledge Base

Build a searchable knowledge base from support articles:

```yaml
queries:
  knowledge-base: |
    {
      "collectionName": "support_articles",
      "keyField": "article_id", 
      "documentTemplate": "Title: {title}\nContent: {content}\nTags: {tags}",
      "metadataFields": ["category", "last_updated", "author"],
      "createCollection": true
    }
```

### 3. Multi-Language Content

Sync content across different languages:

```yaml
queries:
  english-content: |
    {
      "collectionName": "content_en",
      "keyField": "content_id",
      "documentTemplate": "{title}\n{body}",
      "metadataFields": ["language", "published_date", "author"]
    }
  spanish-content: |
    {
      "collectionName": "content_es", 
      "keyField": "content_id",
      "documentTemplate": "{title}\n{body}",
      "metadataFields": ["language", "published_date", "author"]
    }
```

## Monitoring and Troubleshooting

### Check Reaction Status

```bash
drasi list reactions
drasi describe reaction your-reaction-name
```

### View Logs

```bash
kubectl logs -n drasi-system deployment/your-reaction-name-reaction -f
```

### Common Issues

1. **Vector Store Connection Errors**: Verify connection string and network connectivity
2. **Embedding Service Failures**: Check API keys and rate limits
3. **Invalid Document Templates**: Ensure field names in templates match query results
4. **Collection Creation Failures**: Verify vector store permissions and configuration

## Development

### Building Locally

```bash
# Restore dependencies
make restore

# Build the solution
make dotnet-build

# Run tests
make test

# Build Docker image
make build

# Load to kind cluster
make kind-load
```

### Testing

The reaction includes comprehensive unit tests covering:
- Document processing and template rendering
- Embedding generation and batch processing  
- Vector store operations and error handling
- Configuration validation

Run tests with:

```bash
dotnet test Drasi.Reactions.SyncSemanticKernelVectorStore.Tests/
```

## Contributing

This reaction is part of the Drasi platform. Contributions are welcome through the main Drasi repository.

## License

Licensed under the Apache License, Version 2.0.