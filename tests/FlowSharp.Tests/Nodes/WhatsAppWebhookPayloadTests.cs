using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using FlowSharp.Web.Endpoints;
using Xunit;

namespace FlowSharp.Tests.Nodes;

public class WhatsAppWebhookPayloadTests
{
    [Fact]
    public void Normalizes_incoming_message()
    {
        var body = JsonNode.Parse("""
        {
          "object": "whatsapp_business_account",
          "entry": [{
            "changes": [{
              "value": {
                "contacts": [{ "profile": { "name": "Ali" }, "wa_id": "9055" }],
                "messages": [{
                  "from": "9055", "id": "wamid.1", "timestamp": "171",
                  "type": "text", "text": { "body": "merhaba" }
                }]
              }
            }]
          }]
        }
        """);

        WhatsAppWebhookPayload.TryNormalize(body, out var normalized).Should().BeTrue();

        var msg = normalized["messages"]!.AsArray().Should().ContainSingle().Subject!;
        msg["from"]!.GetValue<string>().Should().Be("9055");
        msg["text"]!.GetValue<string>().Should().Be("merhaba");
        msg["messageId"]!.GetValue<string>().Should().Be("wamid.1");
        msg["contactName"]!.GetValue<string>().Should().Be("Ali");
    }

    [Fact]
    public void Normalizes_status_update()
    {
        var body = JsonNode.Parse("""
        {
          "object": "whatsapp_business_account",
          "entry": [{ "changes": [{ "value": {
            "statuses": [{ "id": "wamid.1", "status": "delivered", "recipient_id": "9055", "timestamp": "171" }]
          }}]}]
        }
        """);

        WhatsAppWebhookPayload.TryNormalize(body, out var normalized).Should().BeTrue();

        var status = normalized["statuses"]!.AsArray().Should().ContainSingle().Subject!;
        status["status"]!.GetValue<string>().Should().Be("delivered");
        status["recipientId"]!.GetValue<string>().Should().Be("9055");
    }

    [Fact]
    public void Returns_false_for_non_whatsapp_body()
    {
        var body = JsonNode.Parse("""{ "object": "page", "foo": "bar" }""");
        WhatsAppWebhookPayload.TryNormalize(body, out _).Should().BeFalse();
    }

    [Fact]
    public void Returns_false_for_null_body()
    {
        WhatsAppWebhookPayload.TryNormalize(null, out _).Should().BeFalse();
    }

    // ---- ShouldTrigger: status-only olaylar AI'i tetiklemesin ----

    private static JsonElement Payload(string source, int messages, int statuses)
    {
        var msgs = new JsonArray();
        for (var i = 0; i < messages; i++) msgs.Add(new JsonObject { ["text"] = "m" });
        var sts = new JsonArray();
        for (var i = 0; i < statuses; i++) sts.Add(new JsonObject { ["status"] = "delivered" });
        var root = new JsonObject
        {
            ["source"] = source,
            ["whatsapp"] = new JsonObject { ["messages"] = msgs, ["statuses"] = sts }
        };
        return JsonDocument.Parse(root.ToJsonString()).RootElement;
    }

    [Fact]
    public void Default_filter_skips_status_only_events()
    {
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 0, statuses: 1), "messages").Should().BeFalse();
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 0, statuses: 1), null).Should().BeFalse();
    }

    [Fact]
    public void Messages_filter_triggers_on_incoming_message()
    {
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 1, statuses: 0), "messages").Should().BeTrue();
    }

    [Fact]
    public void Statuses_filter_triggers_only_on_status()
    {
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 0, statuses: 1), "statuses").Should().BeTrue();
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 1, statuses: 0), "statuses").Should().BeFalse();
    }

    [Fact]
    public void All_filter_always_triggers()
    {
        WhatsAppWebhookPayload.ShouldTrigger(Payload("whatsapp", messages: 0, statuses: 1), "all").Should().BeTrue();
    }

    [Fact]
    public void Non_whatsapp_payload_always_triggers()
    {
        WhatsAppWebhookPayload.ShouldTrigger(Payload("webhook", messages: 0, statuses: 0), "messages").Should().BeTrue();
    }
}
