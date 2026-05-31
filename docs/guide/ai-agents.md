# AI Agents & RAG

FlowSharp natively integrates with **Microsoft Semantic Kernel** to support AI agents, chat completion models, and retrieval-augmented generation (RAG).

## AI Agent Node

The `ai.agent` node acts as an intelligent coordinator that can:
*   Connect to various model providers (OpenAI, Anthropic, Gemini, Ollama, etc.).
*   Invoke system/custom tools to fetch data or trigger actions on other systems.
*   Retain conversation memory.

## Vector Store (RAG)

With the `rag.insert` and `rag.query` nodes, you can:
*   Generate embeddings locally using ONNX runtimes.
*   Insert embedded text datasets into SQLite vector stores.
*   Query semantic contexts during workflow executions to construct factual prompts for your AI agents.
