using System.Text.Json.Nodes;
using FluentAssertions;
using FlowSharp.Application.Nodes;
using FlowSharp.Nodes.Core.Logic;
using FlowSharp.Nodes.Data;
using FlowSharp.Tests.Fixtures;
using Xunit;

namespace FlowSharp.Tests.Nodes;

public class DataNodeTests
{
    private static FakeNodeExecutionContext Ctx(JsonObject parameters, params JsonObject[] items) =>
        new(parameters, items.Length == 0 ? [NodeItem.Empty()] : items.Select(NodeItem.From).ToList());

    // ---- DateTime ----
    [Fact]
    public async Task DateTime_format_parses_and_formats()
    {
        var node = new DateTimeNode();
        var ctx = Ctx(new JsonObject
        {
            ["operation"] = "format", ["value"] = "2026-01-15T10:30:00Z",
            ["format"] = "yyyy-MM-dd", ["outputField"] = "d"
        }, new JsonObject());

        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item["d"]!.GetValue<string>().Should().Be("2026-01-15");
    }

    [Fact]
    public async Task DateTime_now_writes_nonempty_value()
    {
        var node = new DateTimeNode();
        var ctx = Ctx(new JsonObject { ["operation"] = "now", ["format"] = "yyyy", ["outputField"] = "y" }, new JsonObject());
        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item["y"]!.GetValue<string>().Should().NotBeNullOrEmpty();
    }

    // ---- Crypto ----
    [Theory]
    [InlineData("sha256", "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("md5", "abc", "900150983cd24fb0d6963f7d28e17f72")]
    public async Task Crypto_hashes_match_known_vectors(string op, string value, string expected)
    {
        var node = new CryptoNode();
        var ctx = Ctx(new JsonObject { ["operation"] = op, ["value"] = value, ["outputField"] = "h" }, new JsonObject());
        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item["h"]!.GetValue<string>().Should().Be(expected);
    }

    [Fact]
    public async Task Crypto_base64_round_trip()
    {
        var enc = new CryptoNode();
        var encoded = (await enc.ExecuteAsync(Ctx(new JsonObject
        {
            ["operation"] = "base64Encode", ["value"] = "merhaba", ["outputField"] = "r"
        }, new JsonObject()))).PrimaryItems.Single().Json["r"]!.GetValue<string>();

        var decoded = (await new CryptoNode().ExecuteAsync(Ctx(new JsonObject
        {
            ["operation"] = "base64Decode", ["value"] = encoded, ["outputField"] = "r"
        }, new JsonObject()))).PrimaryItems.Single().Json["r"]!.GetValue<string>();

        decoded.Should().Be("merhaba");
    }

    // ---- CSV ----
    [Fact]
    public async Task Csv_toCsv_then_fromCsv_round_trips()
    {
        var toCsv = new CsvNode();
        var csv = (await toCsv.ExecuteAsync(Ctx(new JsonObject { ["operation"] = "toCsv" },
            new JsonObject { ["name"] = "Ada", ["age"] = "30" },
            new JsonObject { ["name"] = "Eray", ["age"] = "25" }))).PrimaryItems.Single().Json["csv"]!.GetValue<string>();

        csv.Should().Contain("name").And.Contain("Ada");

        var parsed = (await new CsvNode().ExecuteAsync(Ctx(new JsonObject
        {
            ["operation"] = "fromCsv", ["csvData"] = csv
        }, new JsonObject()))).PrimaryItems;

        parsed.Should().HaveCount(2);
        parsed[0].Json["name"]!.GetValue<string>().Should().Be("Ada");
    }

    // ---- XML <-> JSON ----
    [Fact]
    public async Task Xml_to_json_extracts_elements()
    {
        var node = new XmlJsonNode();
        var ctx = Ctx(new JsonObject
        {
            ["mode"] = "xmlToJson",
            ["xml"] = "<root><name>Ada</name><age>30</age></root>",
            ["outputField"] = "parsed"
        }, new JsonObject());

        var parsed = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json["parsed"]!.AsObject();
        parsed["name"]!.GetValue<string>().Should().Be("Ada");
    }

    // ---- Sort / Limit ----
    [Fact]
    public async Task Sort_orders_numerically_descending()
    {
        var node = new SortNode();
        var ctx = Ctx(new JsonObject { ["field"] = "n", ["order"] = "desc" },
            new JsonObject { ["n"] = "2" }, new JsonObject { ["n"] = "10" }, new JsonObject { ["n"] = "1" });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Select(i => i.Json["n"]!.GetValue<string>()).Should().Equal("10", "2", "1");
    }

    [Fact]
    public async Task Limit_keeps_last_n()
    {
        var node = new LimitNode();
        var ctx = Ctx(new JsonObject { ["max"] = "2", ["keep"] = "last" },
            new JsonObject { ["i"] = "1" }, new JsonObject { ["i"] = "2" }, new JsonObject { ["i"] = "3" });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Select(i => i.Json["i"]!.GetValue<string>()).Should().Equal("2", "3");
    }

    // ---- Aggregate ----
    [Fact]
    public async Task Aggregate_sum_over_field()
    {
        var node = new AggregateNode();
        var ctx = Ctx(new JsonObject { ["operation"] = "sum", ["field"] = "amount" },
            new JsonObject { ["amount"] = "10" }, new JsonObject { ["amount"] = "5" });

        var item = (await node.ExecuteAsync(ctx)).PrimaryItems.Single().Json;
        item["sum"]!.GetValue<double>().Should().Be(15);
        item["count"]!.GetValue<int>().Should().Be(2);
    }

    [Fact]
    public async Task Aggregate_group_by_produces_item_per_group()
    {
        var node = new AggregateNode();
        var ctx = Ctx(new JsonObject { ["operation"] = "count", ["groupBy"] = "cat" },
            new JsonObject { ["cat"] = "a" }, new JsonObject { ["cat"] = "b" }, new JsonObject { ["cat"] = "a" });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Should().HaveCount(2);
        items.First(i => i.Json["cat"]!.GetValue<string>() == "a").Json["count"]!.GetValue<int>().Should().Be(2);
    }

    // ---- SplitOut ----
    [Fact]
    public async Task SplitOut_expands_array_field_into_items()
    {
        var node = new SplitOutNode();
        var ctx = Ctx(new JsonObject { ["field"] = "tags" },
            new JsonObject { ["tags"] = new JsonArray("x", "y", "z") });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Should().HaveCount(3);
        items[0].Json["value"]!.GetValue<string>().Should().Be("x");
    }

    // ---- Filter ----
    [Fact]
    public async Task Filter_drops_items_failing_condition()
    {
        var node = new FilterNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.n }}", ["operation"] = "greaterThan", ["value2"] = "5"
        },
            new JsonObject { ["n"] = 10 }, new JsonObject { ["n"] = 3 }, new JsonObject { ["n"] = 8 });

        var items = (await node.ExecuteAsync(ctx)).PrimaryItems;
        items.Should().HaveCount(2);
    }

    // ---- Switch ----
    [Fact]
    public async Task Switch_routes_by_matching_rule()
    {
        var node = new SwitchNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.type }}",
            ["rules"] = new JsonArray(
                new JsonObject { ["value"] = "a", ["output"] = 0 },
                new JsonObject { ["value"] = "b", ["output"] = 2 })
        },
            new JsonObject { ["type"] = "b" });

        var outputs = (await node.ExecuteAsync(ctx)).Outputs;
        outputs[0].Should().BeEmpty();
        outputs[2].Should().HaveCount(1);
    }

    [Fact]
    public async Task Switch_routes_unmatched_items_to_fallback_output()
    {
        var node = new SwitchNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.type }}",
            ["rules"] = new JsonArray(new JsonObject { ["value"] = "a", ["output"] = 0 })
        },
            new JsonObject { ["type"] = "zzz" });

        var outputs = (await node.ExecuteAsync(ctx)).Outputs;
        // 4 kural portu + 1 fallback; eslesmeyen item sessizce dusurulmez, fallback'e gider.
        outputs.Should().HaveCount(5);
        outputs[0].Should().BeEmpty();
        outputs[4].Should().HaveCount(1);
    }

    [Fact]
    public async Task Switch_fails_on_matched_rule_with_invalid_output()
    {
        var node = new SwitchNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.type }}",
            ["rules"] = new JsonArray(new JsonObject { ["value"] = "b", ["output"] = "abc" })
        },
            new JsonObject { ["type"] = "b" });

        var result = await node.ExecuteAsync(ctx);
        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("output");
    }

    [Fact]
    public async Task Switch_fails_on_matched_rule_with_out_of_range_output()
    {
        var node = new SwitchNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.type }}",
            ["rules"] = new JsonArray(new JsonObject { ["value"] = "b", ["output"] = 7 })
        },
            new JsonObject { ["type"] = "b" });

        var result = await node.ExecuteAsync(ctx);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Switch_accepts_output_given_as_string()
    {
        // "output" string olarak verilse de ("2") dogru porta yonlendirilmeli (eskiden 0'a duserdi).
        var node = new SwitchNode();
        var ctx = Ctx(new JsonObject
        {
            ["value1"] = "{{ $json.type }}",
            ["rules"] = new JsonArray(
                new JsonObject { ["value"] = "a", ["output"] = "0" },
                new JsonObject { ["value"] = "b", ["output"] = "2" })
        },
            new JsonObject { ["type"] = "b" });

        var outputs = (await node.ExecuteAsync(ctx)).Outputs;
        outputs[0].Should().BeEmpty();
        outputs[2].Should().HaveCount(1);
    }
}
