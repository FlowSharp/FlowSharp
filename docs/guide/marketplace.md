# Admin Marketplace

FlowSharp includes a built-in admin marketplace interface where administrators can browse, download, install, and update plugins at runtime without restarting the application.

## How it works

1.  **Retrieve:** The manager fetches the plugins metadata from the configured `OfficialMarketplaceUrl` (GitHub repository).
2.  **Download:** When you click install, the plugin's source code is downloaded into the `plugins/` directory.
3.  **Compile & Load:** The compiler runs Roslyn in-process, outputting an assembly that gets loaded dynamically into a collectible `AssemblyLoadContext`.
4.  **Register:** FlowSharp registers the new `INodeType` instances, adding them to the designer sidebar instantly.
