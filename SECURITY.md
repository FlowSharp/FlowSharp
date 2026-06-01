# Security Guide

This document explains FlowSharp's security-relevant settings and the recommended choices for local, self-hosted, and public deployments.

FlowSharp is a workflow automation platform. Some features intentionally execute user-defined workflows, make outbound HTTP requests, run database queries, and load trusted plugins. Treat access to the admin UI and plugin management as privileged access to the host.

## Quick Checklist

- Set `Security:CredentialEncryptionKey` for both Web and Worker.
- Change the seeded admin password after first login, or override `Seed:Admin`.
- Use `HttpNodes:Exposure = "Public"` when the instance accepts untrusted users or public workflow/webhook input.
- Keep `HttpNodes:Exposure = "Local"` when workflows must call localhost or private network services.
- Install plugins only from repositories you trust.
- Put public deployments behind a reverse proxy with TLS, request size limits, and rate limits.
- Do not expose PostgreSQL or Redis ports to the public internet.
- Consider `Executions:SaveData = "None"` or `"ErrorsOnly"` if workflow payloads can contain secrets.

## Configuration Reference

Example:

```jsonc
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Port=5432;Database=flowsharp_db;Username=flowsharp;Password=<strong-password>"
  },
  "Database": {
    "ApplyMigrationsOnStartup": true
  },
  "Redis": {
    "ConnectionString": "redis:6379,password=<strong-password>"
  },
  "Worker": {
    "RunInWebProcess": false
  },
  "Security": {
    "CredentialEncryptionKey": "<base64-encoded-32-byte-key>"
  },
  "Seed": {
    "Enabled": true,
    "Admin": {
      "Email": "admin@example.local",
      "Password": "<temporary-strong-password>"
    }
  },
  "HttpNodes": {
    "Exposure": "Local",
    "BlockPrivateNetworks": false
  },
  "Executions": {
    "SaveData": "ErrorsOnly",
    "MaxCount": 1000,
    "MaxAgeDays": 30
  },
  "Plugins": {
    "Path": "plugins",
    "OfficialMarketplaceUrl": "https://github.com/FlowSharp/plugins"
  },
  "Rag": {
    "DatabaseDirectory": "App_Data/rag"
  },
  "AllowedHosts": "flowsharp.example.com"
}
```

### `Security:CredentialEncryptionKey`

Required. FlowSharp encrypts credential payloads with AES-GCM. The key must be a base64-encoded 32-byte value and must be identical for the Web and Worker processes.

Generate a key with PowerShell:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
```

Operational notes:

- If the key is missing, the application fails at startup.
- Losing the key makes existing encrypted credentials unreadable.
- Rotating the key requires decrypting existing credentials with the old key and saving them with the new key.
- Do not commit production keys to source control. Prefer environment variables, Docker secrets, Kubernetes secrets, or a secret manager.

Local development note:

- The checked-in local `appsettings.json` files may contain the legacy development key value:
  `sU7uc/MSLu+Aib+HM5nrQXwPifVu+UYwbmu6rfwtYBU=`.
- This value is the explicit form of the old development fallback key and is kept so existing local credentials remain readable.
- Do not use this key for shared, staging, or production deployments. Override it with `Security__CredentialEncryptionKey`.

Environment variable form:

```text
Security__CredentialEncryptionKey=<base64-encoded-32-byte-key>
```

### `Seed`

`Seed:Enabled` controls role and first-admin seeding. The first admin user is created only when there are no users.

Relevant settings:

```jsonc
"Seed": {
  "Enabled": true,
  "Admin": {
    "Email": "admin@example.local",
    "Password": "<temporary-strong-password>"
  }
}
```

Recommendations:

- For a fresh private deployment, use a temporary strong password and change it from the admin/account UI after first login.
- For a hardened production deployment, set the admin email/password via environment variables or secrets.
- Once initial roles/users are created, you may set `Seed:Enabled` to `false` if you do not want startup seeding behavior.

Environment variable form:

```text
Seed__Enabled=true
Seed__Admin__Email=admin@example.local
Seed__Admin__Password=<temporary-strong-password>
```

### `HttpNodes`

HTTP nodes can call external APIs. In public or multi-user deployments, they can also become a path to internal services if not restricted. FlowSharp supports a deployment-mode switch:

```jsonc
"HttpNodes": {
  "Exposure": "Local",
  "BlockPrivateNetworks": false
}
```

`Exposure` values:

- `Local`: default. Allows localhost and private network targets. Use this when workflows need to call services on the same machine, Docker network, LAN, Ollama, local APIs, internal databases, or internal tools.
- `Public`: blocks private and localhost targets for HTTP nodes. Use this when the FlowSharp instance is exposed to users or webhook traffic you do not fully control.

`BlockPrivateNetworks`:

- `false`: normal behavior for `Local`.
- `true`: always blocks private and localhost targets, even if `Exposure` is `Local`.

When blocking is enabled, HTTP nodes reject:

- `localhost`, `127.0.0.0/8`, `::1`
- private IPv4 ranges such as `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`
- link-local ranges such as `169.254.0.0/16` and IPv6 link-local
- carrier-grade NAT range `100.64.0.0/10`
- multicast/reserved ranges

Recommended profiles:

```jsonc
// Local/self-hosted automation profile
"HttpNodes": {
  "Exposure": "Local",
  "BlockPrivateNetworks": false
}
```

```jsonc
// Public/multi-user profile
"HttpNodes": {
  "Exposure": "Public",
  "BlockPrivateNetworks": false
}
```

```jsonc
// Strict profile
"HttpNodes": {
  "Exposure": "Local",
  "BlockPrivateNetworks": true
}
```

### `Executions`

Execution records can contain workflow input, node output, HTTP responses, headers, or business data.

```jsonc
"Executions": {
  "SaveData": "All",
  "MaxCount": 1000,
  "MaxAgeDays": 30
}
```

`SaveData` values:

- `All`: saves node outputs and final workflow output.
- `ErrorsOnly`: saves output data only for failed executions.
- `None`: saves metadata only; output arrays are empty.

Recommendations:

- Use `All` for local development and debugging.
- Use `ErrorsOnly` for most self-hosted production installs.
- Use `None` when workflows process secrets, credentials, personal data, or regulated data.
- Keep `MaxCount` and `MaxAgeDays` bounded to limit stored sensitive history.

### `Plugins`

Plugins are compiled with Roslyn and loaded into the application process. A plugin can run trusted .NET code in-process.

```jsonc
"Plugins": {
  "Path": "plugins",
  "OfficialMarketplaceUrl": "https://github.com/FlowSharp/plugins"
}
```

Recommendations:

- Grant `plugins.manage` only to trusted administrators.
- Install plugins only from repositories you trust and control.
- Prefer a private/approved plugin repository in `OfficialMarketplaceUrl`.
- Review plugin source before installation.
- Treat plugin installation as equivalent to deploying server-side code.

### `Webhooks`

Webhook workflows are exposed at:

```text
/webhook/{path}
```

FlowSharp matches requests by the webhook node's method and path. Runtime exceptions are logged server-side and callers receive a generic error message.

Recommendations:

- Use long, unguessable webhook paths when exposing endpoints publicly.
- Put public webhook endpoints behind a reverse proxy or API gateway for TLS, rate limiting, request size limits, IP allowlists, and optional authentication.
- Avoid echoing secrets in `Respond to Webhook` output.
- Be careful when webhook payloads flow into HTTP, database, AI, or code nodes.

### `ConnectionStrings:DefaultConnection`

PostgreSQL stores users, workflows, queued jobs, encrypted credentials, webhook registrations, and execution records.

Recommendations:

- Use a dedicated database user with the minimum permissions FlowSharp needs.
- Use a strong password.
- Do not expose PostgreSQL directly to the internet.
- Enable TLS between app and database if crossing hosts or networks you do not fully control.
- Back up the database together with the credential encryption key.

### `Redis:ConnectionString`

Redis is used for cross-process workflow event publication. If Redis is unavailable, FlowSharp falls back to in-memory events.

Recommendations:

- Do not expose Redis directly to the internet.
- Use Redis authentication when available.
- Use TLS or a private network for Redis traffic in production.
- If running Web and Worker separately, Redis improves live execution event delivery across processes.

### `Database:ApplyMigrationsOnStartup`

When `true`, FlowSharp applies EF Core migrations at startup.

Recommendations:

- Convenient for local and small self-hosted deployments.
- For controlled production releases, prefer applying migrations as a deployment step before starting the app.
- Avoid running multiple app instances that may try to migrate at the same time unless your deployment process accounts for it.

### `Worker:RunInWebProcess`

When `true`, queued workflow jobs and schedules run inside the Web process.

Recommendations:

- Use `true` for simple local/single-process installs.
- Use `false` with a separate Worker for production or heavier workloads.
- Ensure Web and Worker share the same `Security:CredentialEncryptionKey`, database, Redis, and relevant node settings.

### `Rag:DatabaseDirectory`

RAG vector data is stored in SQLite files under this directory.

Recommendations:

- Store it on persistent storage if RAG data must survive restarts.
- Restrict filesystem permissions to the application identity.
- Treat RAG data as potentially sensitive if inserted workflow content contains private text.

### `AllowedHosts`

ASP.NET Core uses `AllowedHosts` for host filtering.

Recommendations:

- Use a specific hostname in production, for example `flowsharp.example.com`.
- Avoid `*` for internet-facing deployments unless the reverse proxy fully controls host routing.

### `ASPNETCORE_ENVIRONMENT` and Detailed Errors

Production deployments should run with:

```text
ASPNETCORE_ENVIRONMENT=Production
```

Recommendations:

- Do not run internet-facing deployments with `Development`.
- Keep `DetailedErrors` disabled outside local development.
- Production mode enables the production exception handler and HSTS path in the Web app.

### Logging

FlowSharp uses Serilog console and rolling file logs.

Recommendations:

- Protect the `logs` directory.
- Avoid logging secrets from custom nodes and plugins.
- Keep `Microsoft.EntityFrameworkCore.Database.Command` at `Warning` or higher unless debugging, because SQL logging can reveal data.
- Forward production logs to a secured log system with retention policies.

### Docker and Network Exposure

The sample `docker-compose.yml` is convenient for local self-hosting. Review it before internet-facing deployment.

Recommendations:

- Do not publish PostgreSQL port `5432` or Redis port `6379` to public interfaces.
- Replace sample database passwords.
- Provide `Security__CredentialEncryptionKey` through secrets or environment variables.
- Put Web behind a TLS reverse proxy.
- Persist plugin, log, database, and RAG volumes intentionally.

## Roles and Permissions

FlowSharp seeds three roles:

| Role | Intended access |
| --- | --- |
| Admin | Full access, including plugin management. |
| Editor | Workflow read/write/execute and execution read. |
| Viewer | Workflow read and execution read. |

Permission names:

| Permission | Purpose |
| --- | --- |
| `workflows.read` | View workflows. |
| `workflows.write` | Create, edit, and delete workflows. |
| `workflows.execute` | Run workflows. |
| `executions.read` | View execution history. |
| `plugins.manage` | Install, reload, and remove plugins. |

Important notes:

- `plugins.manage` is highly privileged because plugins run trusted server-side code.
- `workflows.write` is powerful because workflows can call external services, database nodes, code nodes, and AI tools.
- `workflows.execute` is powerful when existing workflows contain sensitive credentials or side effects.

## Node-Specific Security Notes

### HTTP Nodes

HTTP nodes use the `HttpNodes` security mode described above. Public deployments should normally use `Exposure = "Public"`.

### Code Node

The JavaScript code node runs with Jint limits for timeout, memory, and statement count. It is still user-supplied code and should be available only to trusted workflow editors.

### Database Nodes

Database nodes use stored credentials. Use database accounts scoped to the minimum required schema and operation.

### AI Nodes

AI nodes may send prompt content and workflow data to model providers configured by credentials. Do not pass secrets or regulated data to external providers unless that is acceptable for your deployment.

### Spreadsheet, XML, and Data Nodes

File and parser nodes can process user-provided content. Use reverse proxy request limits and keep execution retention bounded if files may contain sensitive data.

## Public Deployment Baseline

For an internet-facing instance, start from this posture:

```jsonc
{
  "HttpNodes": {
    "Exposure": "Public",
    "BlockPrivateNetworks": false
  },
  "Executions": {
    "SaveData": "ErrorsOnly",
    "MaxCount": 500,
    "MaxAgeDays": 14
  },
  "Database": {
    "ApplyMigrationsOnStartup": false
  },
  "Seed": {
    "Enabled": false
  },
  "AllowedHosts": "flowsharp.example.com"
}
```

Also configure:

- `Security__CredentialEncryptionKey` as a secret.
- Strong database and Redis credentials.
- TLS at the reverse proxy.
- Rate limits for `/webhook/*` and login endpoints.
- Request body size limits.
- Backups for database and encryption key.

## Local Automation Baseline

For a personal/self-hosted instance that needs localhost and LAN integrations:

```jsonc
{
  "HttpNodes": {
    "Exposure": "Local",
    "BlockPrivateNetworks": false
  },
  "Executions": {
    "SaveData": "All",
    "MaxCount": 1000,
    "MaxAgeDays": 30
  },
  "Worker": {
    "RunInWebProcess": true
  }
}
```

Use firewall rules or reverse proxy authentication if the instance is reachable from other machines.

## Reporting Security Issues

Do not open public issues for suspected vulnerabilities. Report privately to the project maintainers or the organization operating your FlowSharp instance. Include:

- Affected version or commit.
- Deployment mode and relevant security settings.
- Steps to reproduce.
- Expected and actual impact.
- Logs or screenshots with secrets removed.
