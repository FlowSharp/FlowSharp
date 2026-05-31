# Webhooks

Webhooks allow external applications to trigger workflows in FlowSharp.

## Webhook Trigger

When a workflow starts with the `webhook.trigger` node:
*   An endpoint `/webhook/{path}` is registered.
*   Incoming HTTP POST, GET, PUT, or DELETE requests matching the path and method will enqueue or execute the workflow immediately.

## Synchronous Response

You can return a synchronous response back to the caller by ending your flow with the `webhook.response` node. This is highly useful for building custom HTTP API endpoints within your workflow designer.
