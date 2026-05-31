# Contributing to FlowSharp

First of all, thank you for your interest in contributing to FlowSharp! 

To keep the project stable, well-architected, and to prevent contributors from wasting their time on features that might conflict with our design goals, we enforce an **"Issue-Driven Contribution" (Talk First, Code Later)** rule.

---

## 🚫 The Golden Rule: Talk First, Code Later (Issue Dependency)

If you are planning to make any changes to the **Core codebase**, you **MUST** open an issue first to discuss the proposed changes.

*   **Core Codebase** refers to everything inside:
    *   `src/FlowSharp.Web`
    *   `src/FlowSharp.Worker`
    *   `src/FlowSharp.Domain`
    *   `src/FlowSharp.Application`
    *   `src/FlowSharp.Infrastructure`
*   **What is NOT Core?**
    *   Adding new community/plugin nodes under `plugins/` or `src/FlowSharp.Nodes`.
    *   Fixing typos, bugs that have a clear reproduction case, or updating documentation.

> [!IMPORTANT]
> **Any Pull Request (PR) targeting Core code that does not link to an approved and assigned issue will be closed immediately without review.**

### How it works:
1.  **Open an Issue:** Describe what you want to change, why it's necessary, and how you plan to implement it.
2.  **Get Approval:** Wait for the project maintainer to review and approve the concept.
3.  **Get Assigned:** Once approved, the maintainer will assign the issue to you. This signals to the community that you are actively working on it, preventing duplicated work.
4.  **Submit PR:** Link your Pull Request to the assigned issue (e.g., `Closes #123`).

---

## 🔌 Contributing New Nodes & Plugins

If you want to contribute new workflow nodes or plugins, please send your Pull Requests directly to our dedicated community repository: **[FlowSharp Plugins](https://github.com/FlowSharp/plugins)** instead of this main repository!

*   Plugins are isolated and do not affect the core architecture, so they **do not require pre-approval.**
*   Test your node locally and submit a PR directly to the [FlowSharp Plugins](https://github.com/FlowSharp/plugins) repository.
*   Ensure your node implements the `INodeType` contract and has proper exception handling.

## 📝 Code Style & Guidelines

*   Use C# 12 / .NET 10 features.
*   Format your code using the default editor config.
*   Write unit tests where applicable.
