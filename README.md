# FlowSharp

![FlowSharp workflow automation hero](docs/assets/flowsharp-hero.png)

FlowSharp is a node-based workflow automation platform built with **C#**, **.NET 10**, and **Blazor**. It includes a visual workflow designer, executable automation nodes, AI agent support, webhook and schedule triggers, background workers, and runtime-loadable C# plugins.

![FlowSharp workflow designer](docs/assets/flowsharp-designer-mockup.png)

## Highlights

- Visual workflow designer with node palette, connections, parameters, and run status.
- Executable nodes for HTTP, email, PostgreSQL, logic, data transforms, JavaScript, communication services, and AI.
- AI agents powered by Semantic Kernel with model and tool sub-nodes.
- Webhook, manual, schedule, chat, IMAP, workflow, and error triggers.
- Runtime plugin system: drop C# source files into `plugins/` and load new nodes without rebuilding the app.
- ASP.NET Core Identity, role/permission policies, encrypted credentials, SignalR live events, and Serilog logs.

## Libraries Used

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.SemanticKernel` | 1.77.0 | AI Agent and chat models |
| `MudBlazor` | 9.5.0 | UI components |
| `MailKit` | 4.17.0 | SMTP sending and IMAP reading |
| `Jint` | 4.9.2 | Code node - sandboxed JavaScript |
| `CsvHelper` | 33.0.1 | CSV node - read/write CSV |
| `AngleSharp` | 1.1.2 | HTML Extract node - CSS selector parsing |
| `ClosedXML` | 0.104.2 | Spreadsheet node - Excel (.xlsx) reading |
| `Microsoft.Data.Sqlite` | 10.0.0 | RAG - SQLite vector store |
| `SmartComponents.LocalEmbeddings` | 0.1.0-preview10148 | RAG - local/in-process embeddings |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.2 | PostgreSQL + EF Core |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.8 | SQL Server + EF Core |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.8 | SQLite + EF Core |
| `Microsoft.EntityFrameworkCore.Design` / `.Tools` | 10.0.8 | Migration tooling |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | 10.0.8 | Identity storage |
| `Microsoft.CodeAnalysis.CSharp` / Workspaces | 5.3.0 | Roslyn runtime plugin compilation |
| `StackExchange.Redis` | 2.13.17 | Workflow event backplane |
| `Cronos` | 0.13.0 | Cron expression parsing |
| `Serilog.AspNetCore` / Sinks | 10.0.0 / 6.1.1 / 7.0.0 | Logging |

## Quick Start

Run the stack with Docker Compose:

```bash
docker compose up -d --build
```

Open:

```text
http://localhost:8080
```

Default admin account:

```text
admin@flowsharp.local
Admin!2345
```

The default Docker Compose setup uses SQLite for the application database and Redis for cross-process workflow events.

## Local Development

Requirements:

- .NET 10 SDK
- Docker, optional but useful for Redis and database services

Build:

```powershell
dotnet restore
dotnet build
```

Run Web:

```powershell
dotnet run --project src/FlowSharp.Web
```

Run Worker in another terminal:

```powershell
dotnet run --project src/FlowSharp.Worker
```

For single-process development, set:

```json
{
  "Worker": {
    "RunInWebProcess": true
  }
}
```

## Database Support

FlowSharp supports these EF Core providers:

- `Sqlite`
- `Postgres`
- `SqlServer`

Default local SQLite connection string:

```json
{
  "Database": {
    "Provider": "Sqlite",
    "ApplyMigrationsOnStartup": true
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=App_Data/flowsharp.db"
  }
}
```

See [Configuration](docs/guide/configuration.md) for PostgreSQL, SQL Server, Redis, plugins, credentials, and production settings.

## Documentation

- [Getting Started](docs/guide/getting-started.md)
- [Architecture](docs/guide/architecture.md)
- [Configuration](docs/guide/configuration.md)
- [Roles And Permissions](docs/guide/roles-and-permissions.md)
- [Built-in Nodes](docs/guide/built-in-nodes.md)
- [AI Agents](docs/guide/ai-agents.md)
- [Webhooks](docs/guide/webhooks.md)
- [Plugin Development](docs/guide/plugin-development.md)
- [Marketplace](docs/guide/marketplace.md)

## Project Structure

```text
src/
|-- FlowSharp.Web            Blazor UI, Identity, designer, webhooks, marketplace
|-- FlowSharp.Worker         Background worker for queued and scheduled jobs
|-- FlowSharp.Domain         Workflow, execution, queue, credential, and node models
|-- FlowSharp.Application    Interfaces and application contracts
|-- FlowSharp.Infrastructure EF Core, workflow engine, queue, plugins, scheduler
|-- FlowSharp.Nodes          Built-in workflow nodes
```

## License

FlowSharp is licensed under the **Elastic License 2.0 (ELv2)**. See [LICENSE.md](LICENSE.md).

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) before opening a pull request.
