import asyncio
from openai import AsyncAzureOpenAI
from qdrant_client import QdrantClient, models

# --- Configuration ---
# Azure OpenAI Configuration
AZURE_OPENAI_ENDPOINT = "https://amand-man4uhdu-swedencentral.cognitiveservices.azure.com/"
AZURE_OPENAI_API_KEY = "6WYvHiJAItIwDOOZ5r1HsB4ZiASQFLvWanE3xlsXiW4COtXQ3PCIJQQJ99BEACfhMk5XJ3w3AAAAACOGzqCo"
AZURE_OPENAI_API_VERSION = "2024-02-01"

EMBEDDING_DEPLOYMENT_NAME = "text-embedding-3-large"
CHAT_COMPLETION_DEPLOYMENT_NAME = "gpt-4.1"

# Qdrant Configuration
QDRANT_HOST = "localhost"
QDRANT_PORT = 6333
QDRANT_COLLECTION_NAME = "drasi-poc-collection"

# Initialize Azure OpenAI Client
azure_client = AsyncAzureOpenAI(
    azure_endpoint=AZURE_OPENAI_ENDPOINT,
    api_key=AZURE_OPENAI_API_KEY,
    api_version=AZURE_OPENAI_API_VERSION
)

# Initialize Qdrant Client
qdrant_client = QdrantClient(host=QDRANT_HOST, port=QDRANT_PORT)


async def get_embedding(text: str) -> list[float]:
    """Generates an embedding for the given text using Azure OpenAI."""
    try:
        response = await azure_client.embeddings.create(
            model=EMBEDDING_DEPLOYMENT_NAME,
            input=text
        )
        return response.data[0].embedding
    except Exception as e:
        print(f"Error generating embedding: {e}")
        raise


async def search_qdrant(query_vector: list[float], top_k: int = 3) -> list[str]:
    """Searches Qdrant for similar vectors and returns their context."""
    try:
        search_result = qdrant_client.search(
            collection_name=QDRANT_COLLECTION_NAME,
            query_vector=query_vector,
            limit=top_k,
            with_payload=True
        )
        
        contexts = []
        for hit in search_result:
            if hit.payload and 'text' in hit.payload:
                contexts.append(hit.payload['text'])
        return contexts
    except Exception as e:
        print(f"Error searching Qdrant: {e}")
        raise


async def get_chat_completion(user_question: str, context: str) -> str:
    """Gets a chat completion from Azure OpenAI based on the user question and context."""
    system_message = "You are a helpful AI assistant. Answer the user's question based on the provided context."
    prompt = f"""
    Context:
    ---
    {context}
    ---
    Question: {user_question}
    
    Answer:
    """
    try:
        response = await azure_client.chat.completions.create(
            model=CHAT_COMPLETION_DEPLOYMENT_NAME,
            messages=[
                {"role": "system", "content": system_message},
                {"role": "user", "content": prompt}
            ],
            temperature=0.7,
            max_tokens=800
        )
        return response.choices[0].message.content.strip()
    except Exception as e:
        print(f"Error getting chat completion: {e}")
        raise


async def rag_pipeline(user_question: str):
    """Runs the RAG pipeline."""
    print(f"User Question: {user_question}\n")

    # 1. Generate embedding for the user question
    print("Step 1: Generating embedding for the question...")
    question_embedding = await get_embedding(user_question)
    print("Embedding generated.\n")

    # 2. Search Qdrant for relevant documents
    print("Step 2: Searching Qdrant for relevant context...")
    retrieved_contexts = await search_qdrant(question_embedding)
    if not retrieved_contexts:
        print("No relevant context found in Qdrant.\n")
        context_for_llm = "No specific context found in the knowledge base."
    else:
        print(f"Retrieved {len(retrieved_contexts)} context(s):\n")
        for i, ctx in enumerate(retrieved_contexts):
            print(f"Context {i+1}: {ctx}\n")
        context_for_llm = "\n\n".join(retrieved_contexts)
    
    # 3. Generate an answer using the LLM with the retrieved context
    print("Step 3: Generating answer with LLM...")
    answer = await get_chat_completion(user_question, context_for_llm)
    print("\n--- LLM Answer ---")
    print(answer)
    print("--------------------")


if __name__ == "__main__":
    test_question = "Which is the temperature on freezer 1?"
    asyncio.run(rag_pipeline(test_question))