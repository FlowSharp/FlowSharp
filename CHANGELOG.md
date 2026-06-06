# Changelog

All notable changes to FlowSharp are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is `0.x`, minor releases may include breaking changes.

## [0.1.0] - 2026-06-06

First public release.

> [!IMPORTANT]
> Before exposing FlowSharp anywhere reachable, change the defaults:
> set your own `Security:CredentialEncryptionKey` (a unique Base64, 32-byte key, identical on Web and Worker)
> and change the seeded admin password (`admin@flowsharp.local` / `Admin!2345`).
> See [SECURITY.md](SECURITY.md).

### Added

- **Visual workflow designer** — node palette, connections, parameters, live run status, sticky notes, pinned data, and partial / up-to-node execution.
- **Workflow engine** — graph execution with topological ordering, multi-output routing (IF / Switch), loop regions with batching (incl. nested loops), and a database-backed job queue.
- **Built-in nodes** across HTTP, databases (PostgreSQL, SQL Server, MySQL, SQLite via connection → operation nodes), data transforms, logic/flow, communication (Slack, Discord, Telegram, WhatsApp, Email), and a sandboxed JavaScript Code node.
- **Triggers** — manual, schedule (cron), webhook, IMAP, chat, WhatsApp, execute-workflow, and error triggers; synchronous webhook responses with WhatsApp/Meta verification and event filtering.
- **AI agents & RAG** — Semantic Kernel agents with model, tool (incl. MCP), and memory sub-nodes; standalone chat nodes for OpenAI, Azure OpenAI, Anthropic, Gemini, Groq, Mistral, Cohere, Hugging Face, OpenRouter, and Ollama; local-embedding vector store with per-workspace SQLite databases.
- **Runtime plugin system** — drop C# source into `plugins/`; compiled with Roslyn into a collectible load context, with an in-app admin marketplace that installs from GitHub.
- **Security & multi-user** — ASP.NET Core Identity, role/permission policies, AES-GCM encrypted credentials, owner-scoped data isolation, per-user rate limiting, and SSRF egress controls for HTTP nodes.
- **Persistence** — Entity Framework Core with SQLite (default), PostgreSQL, and SQL Server; provider-specific migration assemblies applied automatically on startup.
- **Operations** — SignalR live execution events (Redis backplane), configurable execution data retention and large-output blob offload, Serilog logging, optional OpenTelemetry traces/metrics, and liveness/readiness/detailed health endpoints.
- **Deployment** — multi-stage Dockerfile (Web + Worker targets), Docker Compose stack (Redis, Postgres, PgBouncer), and Kubernetes manifests with HPA / KEDA queue-depth autoscaling.
- **Documentation** — full guide site (VitePress) and bilingual READMEs (English + Turkish).

[0.1.0]: https://github.com/FlowSharp/FlowSharp/releases/tag/v0.1.0
