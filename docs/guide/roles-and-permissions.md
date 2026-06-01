# Roles And Permissions

FlowSharp uses ASP.NET Core Identity roles plus permission claims. Each permission is registered as an authorization policy at startup, and policies require a `permission` claim with the same value.

## Seeded Roles

FlowSharp seeds three roles on startup:

| Role | Purpose |
|---|---|
| `Admin` | Full access to workflows, executions, and plugin management. |
| `Editor` | Can create, edit, and execute workflows. |
| `Viewer` | Can view workflows and execution history. |

## Permission Matrix

| Permission | Description | Admin | Editor | Viewer |
|---|---|:---:|:---:|:---:|
| `workflows.read` | View workflow list and workflow details. | Yes | Yes | Yes |
| `workflows.write` | Create, edit, and save workflows and credentials. | Yes | Yes | No |
| `workflows.execute` | Run workflows from the UI. | Yes | Yes | No |
| `executions.read` | View workflow execution history and logs. | Yes | Yes | Yes |
| `plugins.manage` | Access marketplace and manage plugins. | Yes | No | No |

## Where Permissions Are Defined

Permissions live in:

```text
src/FlowSharp.Domain/Security/AppPermissions.cs
```

Role-to-permission seeding lives in:

```text
src/FlowSharp.Infrastructure/Identity/IdentitySeeder.cs
```

Authorization policies are registered during web startup:

```text
src/FlowSharp.Web/Program.cs
```

## Protected Areas

| Area | Required Permission |
|---|---|
| Workflows page | `workflows.read` |
| Workflow designer | `workflows.write` |
| Credentials page | `workflows.write` |
| Executions page | `executions.read` |
| Marketplace page | `plugins.manage` |
| Marketplace navigation links | `plugins.manage` |

## First Admin User

When seeding is enabled and no users exist, FlowSharp creates the first admin user from configuration:

```json
{
  "Seed": {
    "Enabled": true,
    "Admin": {
      "Email": "admin@flowsharp.local",
      "Password": "Admin!2345"
    }
  }
}
```

The first user is assigned to the `Admin` role.
