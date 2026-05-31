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
