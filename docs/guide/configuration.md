# Configuration

FlowSharp is configured using standard `appsettings.json` parameters.

## Core Settings

Below is the standard configuration template:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=flowsharp_db;Username=postgres;Password=Postgres"
  },
  "Database": {
    "Provider": "Postgres",
    "ApplyMigrationsOnStartup": false
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "Worker": {
    "RunInWebProcess": false
  },
  "Plugins": {
    "Path": "plugins",
    "OfficialMarketplaceUrl": "https://github.com/FlowSharp/plugins"
  }
}
```

### Options

*   **`Worker:RunInWebProcess`**: When set to `true`, the background job runner processes jobs inside the web application context (single-process mode). Set to `false` for high-performance multi-process setups where a separate worker process handles the load.
*   **`Plugins:OfficialMarketplaceUrl`**: Points to the GitHub repository hosting community plugins. FlowSharp uses this URL to fetch, download, and dynamically hot-load node plugins.

## Database Providers

Set `Database:Provider` to one of:

*   **`Postgres`**: Default production provider. Uses the existing EF Core migrations.
*   **`SqlServer`**: Uses SQL Server via EF Core. On startup, FlowSharp creates the schema when migrations are enabled.
*   **`Sqlite`**: Local and single-node friendly provider. On startup, FlowSharp creates the schema when migrations are enabled.

### SQL Server

```jsonc
{
  "Database": {
    "Provider": "SqlServer",
    "ApplyMigrationsOnStartup": true
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=FlowSharpDb;User Id=sa;Password=Your_password123;TrustServerCertificate=True"
  }
}
```

### SQLite

```jsonc
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
