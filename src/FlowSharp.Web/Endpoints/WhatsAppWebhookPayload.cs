using System.Text.Json;
using System.Text.Json.Nodes;

namespace FlowSharp.Web.Endpoints;

/// <summary>
/// Meta WhatsApp Cloud API webhook govdesini (entry[].changes[].value) workflow'larda kolay
/// kullanilabilir bir bicime duzlestirir: gelen mesajlar ve durum guncellemeleri ayri diziler.
/// Ham payload cagiran tarafindan korunur (normalize + raw).
/// </summary>
public static class WhatsAppWebhookPayload
{
    /// <summary>
    /// <paramref name="body"/> bir <c>whatsapp_business_account</c> olayiysa normalize edip true doner.
    /// Aksi halde false doner (generic webhook davranisi korunur).
    /// </summary>
    public static bool TryNormalize(JsonNode? body, out JsonObject normalized)
    {
        normalized = new JsonObject();

        if (body is not JsonObject root ||
            root["object"]?.GetValue<string>() is not "whatsapp_business_account")
        {
            return false;
        }

        var messages = new JsonArray();
        var statuses = new JsonArray();

        foreach (var change in EnumerateChanges(root))
        {
            var value = change["value"] as JsonObject;
            if (value is null)
            {
                continue;
            }

            var contact = (value["contacts"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault();
            var contactName = contact?["profile"]?["name"]?.GetValue<string>();
            // Gonderen kimligi: klasik "wa_id" (telefon) veya yeni "user_id".
            var contactId = contact?["wa_id"]?.GetValue<string>() ?? contact?["user_id"]?.GetValue<string>();

            if (value["messages"] is JsonArray incoming)
            {
                foreach (var message in incoming.OfType<JsonObject>())
                {
                    messages.Add(NormalizeMessage(message, contactName, contactId));
                }
            }

            if (value["statuses"] is JsonArray updates)
            {
                foreach (var status in updates.OfType<JsonObject>())
                {
                    statuses.Add(NormalizeStatus(status));
                }
            }
        }

        normalized["messages"] = messages;
        normalized["statuses"] = statuses;
        return true;
    }

    /// <summary>
    /// Olusturulmus webhook payload'ina gore bu olayin workflow'u tetikleyip tetiklemeyecegini belirler.
    /// Yalniz WhatsApp olaylarina (source=whatsapp) uygulanir; diger webhook'lar her zaman tetikler.
    /// <paramref name="eventFilter"/>: messages | statuses | all (varsayilan: messages).
    /// </summary>
    public static bool ShouldTrigger(JsonElement payload, string? eventFilter)
    {
        // WhatsApp olayi degilse (generic webhook) her zaman calistir.
        if (!payload.TryGetProperty("source", out var source) || source.GetString() != "whatsapp")
        {
            return true;
        }

        var hasMessages = CountArray(payload, "messages") > 0;
        var hasStatuses = CountArray(payload, "statuses") > 0;

        return (eventFilter?.ToLowerInvariant()) switch
        {
            "all" => true,
            "statuses" => hasStatuses,
            _ => hasMessages // "messages" ve varsayilan: yalniz gelen mesaj varsa tetikle.
        };
    }

    private static int CountArray(JsonElement payload, string property) =>
        payload.TryGetProperty("whatsapp", out var whatsapp)
            && whatsapp.TryGetProperty(property, out var array)
            && array.ValueKind == JsonValueKind.Array
            ? array.GetArrayLength()
            : 0;

    private static IEnumerable<JsonObject> EnumerateChanges(JsonObject root)
    {
        if (root["entry"] is not JsonArray entries)
        {
            yield break;
        }

        foreach (var entry in entries.OfType<JsonObject>())
        {
            if (entry["changes"] is JsonArray changes)
            {
                foreach (var change in changes.OfType<JsonObject>())
                {
                    yield return change;
                }
            }
        }
    }

    private static JsonObject NormalizeMessage(JsonObject message, string? contactName, string? contactId)
    {
        var type = message["type"]?.GetValue<string>();
        // Klasik telefon mesajlarinda "from" (telefon no); yeni kimlik formatinda "from_user_id".
        var from = message["from"]?.GetValue<string>() ?? message["from_user_id"]?.GetValue<string>() ?? contactId;
        return new JsonObject
        {
            ["from"] = from,
            ["messageId"] = message["id"]?.GetValue<string>(),
            ["timestamp"] = message["timestamp"]?.GetValue<string>(),
            ["type"] = type,
            ["text"] = ExtractText(message, type),
            ["contactName"] = contactName
        };
    }

    private static string? ExtractText(JsonObject message, string? type) => type switch
    {
        "text" => message["text"]?["body"]?.GetValue<string>(),
        "button" => message["button"]?["text"]?.GetValue<string>(),
        "interactive" => message["interactive"]?["button_reply"]?["title"]?.GetValue<string>()
            ?? message["interactive"]?["list_reply"]?["title"]?.GetValue<string>(),
        _ => null
    };

    private static JsonObject NormalizeStatus(JsonObject status) => new()
    {
        ["status"] = status["status"]?.GetValue<string>(),
        ["messageId"] = status["id"]?.GetValue<string>(),
        ["recipientId"] = status["recipient_id"]?.GetValue<string>(),
        ["timestamp"] = status["timestamp"]?.GetValue<string>()
    };
}
