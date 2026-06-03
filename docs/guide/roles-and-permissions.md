# Roles And Permissions

FlowSharp uses ASP.NET Core Identity roles plus permission claims. Each permission is registered as an authorization policy at startup, and policies require a `permission` claim with the same value.

## Seeded Roles

FlowSharp seeds four roles on startup:

| Role | Purpose |
|---|---|
| `Admin` | Full access, including credential and plugin management. |
| `Editor` | Can create, edit, and execute workflows, and manage their own credentials. |
| `Member` | Default role assigned to self-registered users; same capabilities as `Editor`. |
| `Viewer` | Can view workflows and execution history. |

`Editor` and `Member` share the same permission set. The distinction is operational: `Member` is granted automatically when a user signs up through self-registration, whereas `Editor` is assigned by an administrator.

## Permission Matrix

| Permission | Description | Admin | Editor | Member | Viewer |
|---|---|:---:|:---:|:---:|:---:|
| `workflows.read` | View workflow list and workflow details. | Yes | Yes | Yes | Yes |
| `workflows.write` | Create, edit, and save workflows. | Yes | Yes | Yes | No |
| `workflows.execute` | Run workflows from the UI. | Yes | Yes | Yes | No |
| `executions.read` | View workflow execution history and logs. | Yes | Yes | Yes | Yes |
| `credentials.manage` | Create, edit, delete, and view stored credentials. | Yes | Yes | Yes | No |
| `plugins.manage` | Access marketplace and manage plugins. | Yes | No | No | No |

`credentials.manage` is owner-scoped for non-administrators: `Editor` and `Member` manage **only their own** credentials, while `Admin` manages all. `plugins.manage` remains `Admin`-only because plugins run trusted server-side code.

## Data Isolation (Ownership)

Workflows and credentials carry an owner. Non-administrators see and manage **only their own** records:

- A user's workflow list, designer, executions, and dashboard counts are scoped to records they own.
- The Credentials page is owner-scoped: `Editor` and `Member` see and manage only the credentials they created.
- Credentials are resolved at execution time only when the credential owner matches the workflow owner, preventing cross-tenant secret access.
- `Admin` is exempt from ownership filtering and can see all records (the Workflows and Credentials pages show an admin's own records and other users' records in separate sections).

This makes self-registration safe in multi-user deployments: each user operates within an isolated workspace.

## Where Permissions Are Defined

Permissions live in:

```text
src/FlowSharp.Domain/Security/AppPermissions.cs
```

Role-to-permission seeding and self-registration role assignment live in:

```text
src/FlowSharp.Infrastructure/Identity/IdentitySeeder.cs
src/FlowSharp.Web/Components/Account/Pages/Register.razor
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
| Credentials page | `credentials.manage` |
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

## Self-Registration

Users who register through the sign-up page are automatically granted the `Member` role and operate within their own isolated workspace (see [Data Isolation](#data-isolation-ownership)).

Account confirmation is governed by `Identity:RequireConfirmedAccount`:

- `false` — users can sign in immediately after registering (no email verification).
- `true` — a confirmation email is sent and must be acknowledged before sign-in. This requires a working SMTP configuration (see [Configuration](configuration.md)).
