using System.Text.Json.Nodes;
using FluentAssertions;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Ai.Tools;
using FlowSharp.Tests.Fixtures;
using Xunit;

namespace FlowSharp.Tests.Nodes;

public class McpToolNodeTests
{
    [Fact]
    public void Definition_exposes_ai_tool_output_and_credential_schema()
    {
        var node = new McpToolNode();

        node.Definition.Key.Should().Be("tool.mcp");
        node.Definition.Category.Should().Be(NodeCategory.Ai);
        node.Definition.SubOutputPorts.Should().ContainSingle()
            .Which.Type.Should().Be(NodePortType.AiTool);

        node.CredentialSchemas.Should().ContainSingle()
            .Which.Type.Should().Be(McpToolNode.CredentialTypeKey);
    }

    [Fact]
    public async Task Execute_fails_gracefully_when_server_url_missing()
    {
        var node = new McpToolNode();
        var ctx = new FakeNodeExecutionContext(parameters: new JsonObject());

        var result = await node.ExecuteAsync(ctx);

        result.Succeeded.Should().BeTrue();
        result.PrimaryItems.Single().Json["error"]!.GetValue<string>().Should().Contain("serverUrl");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAllowedTools_returns_null_when_blank(string? csv)
    {
        McpConnection.ParseAllowedTools(csv).Should().BeNull();
    }

    [Fact]
    public void ParseAllowedTools_splits_trims_and_is_case_insensitive()
    {
        var set = McpConnection.ParseAllowedTools(" search , fetch ,");

        set.Should().NotBeNull();
        set!.Should().BeEquivalentTo(["search", "fetch"]);
        set.Contains("SEARCH").Should().BeTrue();
    }
}
