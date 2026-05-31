# Plugin Development

FlowSharp features a **Roslyn-powered hot-loadable** plugin system. This allows developers to write new nodes in C# and load them into FlowSharp without restarting or rebuilding the main application.

## Writing a Node

To create a new node, implement a class inheriting from `PerItemNodeType` or `NodeTypeBase` inside a folder under the `plugins/` directory.

### Example: `plugins/Sample/HelloNode.cs`

```csharp
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace Community.Sample;

public sealed class HelloNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "community.hello",
        DisplayName: "Hello (Plugin)",
        Category: NodeCategory.Core,
        Kind: NodeKind.Action,
        Description: "Adds a greeting to the item.",
        Parameters: [ 
            new NodeParameterDefinition("name", "Name", NodeParameterType.String, DefaultValue: "World") 
        ],
        Tags: ["community"], 
        Icon: "sparkles", 
        Color: "#9b51e0"
    );

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var name = context.GetString("name", index) ?? "World";
        var output = (JsonObject)item.Json.DeepClone();
        output["greeting"] = $"Hello, {name}!";
        return Task.FromResult<NodeItem?>(NodeItem.From(output));
    }
}
```

---

## Contributing Plugins

We host all community-contributed plugins in a dedicated repository:
👉 **[FlowSharp Plugins Repository](https://github.com/FlowSharp/plugins)**

Please open your pull requests directly on the plugins repository. Once approved, plugins will automatically become available in the official admin marketplace!
