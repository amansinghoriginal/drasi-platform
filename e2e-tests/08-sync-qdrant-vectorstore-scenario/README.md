# Qdrant Vector Store E2E Test

This test validates the Semantic Kernel Vector Store reaction with Qdrant as the vector database.

## Prerequisites

### 1. Azure OpenAI API Key
The test requires an Azure OpenAI API key with access to an embedding model.

Set the environment variable before running the test:
```bash
export AZURE_OPENAI_KEY='your-azure-openai-api-key'
```

## Running the Test

```bash
# Set the API key
export AZURE_OPENAI_KEY='your-key-here'

# Run the test
npm test qdrant-vectorstore.test.js
```
