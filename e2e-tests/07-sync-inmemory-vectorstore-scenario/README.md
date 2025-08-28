# E2E Tests: InMemory Vector Store Synchronization

## Overview
This test suite validates the **Semantic Kernel Vector Store Reaction** (`reactions/semantickernel/sync-vectorstore`) using an **InMemory vector store** backend with **Azure OpenAI embeddings**.

The reaction continuously synchronizes Drasi query results to a vector store, generating embeddings for each result row using configurable Handlebars templates.

## What's Being Tested

### Core Reaction Flow
```
PostgreSQL → Drasi Queries → SK Vector Store Reaction → InMemory Vector Store
     ↓              ↓                    ↓                        ↓
  Changes     Query Results      Generate Embeddings      Store Documents
```

### Test Scenarios

#### 1. **Initial State Sync** ([inmemory-vectorstore.test.js:193-233](inmemory-vectorstore.test.js#L193-L233))
Verifies that existing database rows are:
- Detected by Drasi queries
- Processed to generate embeddings via Azure OpenAI
- Stored in the InMemory vector store

```javascript
// Verify 5 initial products are synced
expect(queryData).toHaveLength(5);
const productIds = queryData.map(item => item.id).sort();
expect(productIds).toEqual([1, 2, 3, 4, 5]);
```

#### 2. **Insert Operations** ([inmemory-vectorstore.test.js:235-300](inmemory-vectorstore.test.js#L235-L300))
Tests that new database inserts:
- Trigger real-time change detection
- Generate new embeddings
- Add documents to vector store

Single insert example:
```sql
INSERT INTO products (id, name, description, category_id) 
VALUES (101, "Test Product", "A test product for e2e testing", 1)
```

#### 3. **Update Operations** ([inmemory-vectorstore.test.js:302-369](inmemory-vectorstore.test.js#L302-L369))
Validates that updates:
- Regenerate embeddings with new content
- Replace existing documents (same key, new embedding)
- Cascade through joined queries

```javascript
// Update triggers re-embedding
await dbClient.query(
  "UPDATE products SET description = $1 WHERE id = $2",
  ["Updated test product description", 101]
);
```

#### 4. **Delete Operations** ([inmemory-vectorstore.test.js:371-423](inmemory-vectorstore.test.js#L371-L423))
Ensures deletions:
- Remove documents from vector store
- Handle cascade deletes in join queries

## Test Configuration

### Queries Tested
1. **Simple Query** (`sk-products-query`): Single table query
   ```cypher
   MATCH (p:products)
   RETURN p.id AS id, p.name AS name, p.description AS description, p.category_id AS category_id
   ```

2. **Join Query** (`sk-products-with-category-query`): Multi-table join with explicit relationship
   ```cypher
   MATCH (p:products)-[:HAS_CATEGORY]->(c:categories)
   RETURN p.id AS product_id, p.name AS product_name, 
          p.description AS product_description,
          c.name AS category_name, c.description AS category_description
   ```

### Reaction Configuration ([reactions.yaml](reactions.yaml))
```yaml
apiVersion: v1
kind: Reaction
name: sk-inmemory-simple-reaction
spec:
  kind: SyncSemanticKernelVectorStore
  queries:
    sk-products-query:
      keyField: id
      contentTemplate: "Product: {{name}} - {{description}}"
  properties:
    vectorStoreType: InMemory
    embeddingService:
      type: AzureOpenAI
      deploymentName: "text-embedding-3-large"
```

### Key Components
- **Vector Store**: InMemory (non-persistent)
- **Embedding Model**: Azure OpenAI `text-embedding-3-large` (3072 dimensions)
- **Template Engine**: Handlebars for content generation
- **Database**: PostgreSQL with logical replication

## Test Data

### Initial Data ([resources.yaml:19-31](resources.yaml#L19-L31))
```sql
-- 3 categories
INSERT INTO categories (id, name, description) VALUES 
  (1, 'Electronics', 'Electronic devices and accessories'),
  (2, 'Books', 'Books and publications'),
  (3, 'Clothing', 'Apparel and accessories');

-- 5 products
INSERT INTO products (id, name, description, category_id) VALUES 
  (1, 'Laptop Pro', 'High-performance laptop...', 1),
  (2, 'Wireless Mouse', 'Ergonomic wireless mouse...', 1),
  -- ... more products
```

### Test-Specific Data
- IDs 101-106: Used for insert/update/delete tests
- Cleaned up after each test to maintain isolation

## Verification Methods

Since InMemory store doesn't expose an API, verification happens through:
1. **SignalR monitoring** - Tracks query result changes as proxy for vector store state
2. **Reaction logs** - Checks for "Generated embedding" log entries
3. **Query state validation** - Ensures Drasi queries reflect expected state

## Running the Tests

### Prerequisites
```bash
# Set Azure OpenAI credentials
export AZURE_OPENAI_KEY="your-api-key"
```

### Execute Tests
```bash
cd e2e-tests
npm test -- 07-sync-inmemory-vectorstore-scenario/inmemory-vectorstore.test.js
```

## Expected Behavior

✅ **Success Indicators:**
- All 5 initial products generate embeddings
- Insert/Update/Delete operations reflect in vector store within 30 seconds
- Join query updates cascade correctly
- No duplicate documents created

❌ **Common Failures:**
- Missing Azure OpenAI key: Set `AZURE_OPENAI_KEY` environment variable
- Timeout errors: Initial sync may take up to 30 seconds
- Port conflicts: Ensure ports 5432 aren't in use

## Architecture Notes

The InMemory vector store:
- Stores embeddings in process memory
- Does NOT persist across reaction restarts
- Useful for testing and development scenarios
- No external dependencies beyond the reaction itself

For production use cases requiring persistence, see the [Qdrant test suite](../08-sync-qdrant-vectorstore-scenario/README.md).