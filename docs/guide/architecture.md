# Architecture

FlowSharp is structured into multiple layers following clean architecture principles to keep logic decoupled from presentation and infrastructure.

## Project Structure

```
src/
├─ FlowSharp.Web              # Blazor UI, Identity, webhook endpoint, marketplace
├─ FlowSharp.Worker           # BackgroundService that processes jobs from the queue
├─ FlowSharp.Domain           # Core entities: Workflow, execution, queue, node
├─ FlowSharp.Application      # Contracts: INodeType, INodeRegistry, IPluginManager, expressions
├─ FlowSharp.Infrastructure   # EF Core, queue database engine, plugin manager (Roslyn), scheduler
├─ FlowSharp.Nodes            # Built-in nodes library
└─ Migrations/                # Provider-specific EF Core migration assemblies
   ├─ FlowSharp.Migrations.Sqlite
   ├─ FlowSharp.Migrations.Postgres
   └─ FlowSharp.Migrations.SqlServer
```

Each database provider has its own native migration assembly, selected at runtime from configuration. See [Database & Migrations](database-migrations.md).

---

## Technical Stack

| Component | Technology | Description |
|---|---|---|
| **UI** | Blazor Web App (Interactive Server) | Live dashboard, visual designer canvas. |
| **Backend** | ASP.NET Core (.NET 10) | Host API, websocket connections. |
| **Realtime Updates** | SignalR + Redis | Updates node status execution live in the browser. |
| **Database** | PostgreSQL, SQL Server, or SQLite + EF Core | Stores workflows, JSON definitions, executions, and queue jobs. |
| **Queue** | DB-backed queue | Provider-agnostic `workflow_jobs` table (SQLite, PostgreSQL, or SQL Server) for reliable job processing. |
| **Worker** | Background Service | Separate daemon consuming jobs from the database queue. |
| **Plugins** | Roslyn C# Compiler | Hot-compiles C# source code files and loads assemblies dynamically. |
| **AI** | Semantic Kernel | Orchestrates OpenAI, Azure, Gemini, Anthropic models and tools. |
