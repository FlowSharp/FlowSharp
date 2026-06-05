using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Helpers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace FlowSharp.Nodes.Communication.WhatsApp;

/// <summary>
/// WhatsApp Cloud API (Meta Graph) uzerinden mesaj gonderir. <c>messageType</c> ile
/// text / template / media (image,document) / interactive (button,list) / location / reaction
/// desteklenir. Credential "whatsAppApi": accessToken (Bearer) + phoneNumberId.
/// </summary>
public sealed class WhatsAppMessageNode : PerItemNodeType, IProvidesCredentials
{
    public const string CredentialTypeKey = WhatsAppCredentials.TypeKey;

    public IEnumerable<CredentialSchema> CredentialSchemas => [WhatsAppCredentials.Schema];

    private static NodeParameterDefinition When(NodeParameterDefinition p, params string[] types) =>
        p with { ShowWhen = new ParameterCondition("messageType", types) };

    public override NodeDefinition Definition { get; } = new(
        "whatsapp.message", "WhatsApp", NodeCategory.Communication, NodeKind.Action,
        "WhatsApp Cloud API uzerinden mesaj gonderir (text, template, medya, interaktif, konum, reaction).",
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "whatsAppApi tipli credential (accessToken + phoneNumberId)"),
            new NodeParameterDefinition("to", "To", NodeParameterType.String, IsRequired: true,
                HelpText: "Alici telefon numarasi. Gelen mesaja yanit: {{$json.whatsapp.messages[0].from}}"),
            new NodeParameterDefinition("messageType", "Message Type", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "text",
                Options: ["text", "template", "image", "document", "interactiveButtons", "interactiveList", "location", "reaction"]),

            // text
            When(new NodeParameterDefinition("text", "Text", NodeParameterType.Text, IsRequired: true,
                HelpText: "Ornek: Merhaba {{$json.whatsapp.messages[0].contactName}}! Mesajin: {{$json.whatsapp.messages[0].text}}"), "text"),
            When(new NodeParameterDefinition("previewUrl", "Link Preview", NodeParameterType.Boolean, DefaultValue: "false"), "text"),

            // template
            When(new NodeParameterDefinition("templateName", "Template Name", NodeParameterType.String, IsRequired: true), "template"),
            When(new NodeParameterDefinition("languageCode", "Language Code", NodeParameterType.String, DefaultValue: "en_US"), "template"),
            When(new NodeParameterDefinition("bodyParams", "Body Parameters", NodeParameterType.Json,
                HelpText: "Opsiyonel JSON dizisi, ornek: [\"Ali\", \"123\"]"), "template"),

            // media (image/document)
            When(new NodeParameterDefinition("mediaLink", "Media URL", NodeParameterType.Url, IsRequired: true), "image", "document"),
            When(new NodeParameterDefinition("caption", "Caption", NodeParameterType.Text), "image", "document"),
            When(new NodeParameterDefinition("filename", "File Name", NodeParameterType.String), "document"),

            // interactive
            When(new NodeParameterDefinition("bodyText", "Body Text", NodeParameterType.Text, IsRequired: true,
                HelpText: "Ornek: Merhaba {{$json.whatsapp.messages[0].contactName}}, nasil yardimci olabilirim?"), "interactiveButtons", "interactiveList"),
            When(new NodeParameterDefinition("buttons", "Buttons", NodeParameterType.Json, IsRequired: true,
                HelpText: "JSON: [{\"id\":\"yes\",\"title\":\"Evet\"}] (en fazla 3)"), "interactiveButtons"),
            When(new NodeParameterDefinition("buttonText", "List Button Text", NodeParameterType.String, IsRequired: true, DefaultValue: "Sec"), "interactiveList"),
            When(new NodeParameterDefinition("sections", "Sections", NodeParameterType.Json, IsRequired: true,
                HelpText: "JSON: [{\"title\":\"...\",\"rows\":[{\"id\":\"1\",\"title\":\"...\"}]}]"), "interactiveList"),

            // location
            When(new NodeParameterDefinition("latitude", "Latitude", NodeParameterType.Number, IsRequired: true), "location"),
            When(new NodeParameterDefinition("longitude", "Longitude", NodeParameterType.Number, IsRequired: true), "location"),
            When(new NodeParameterDefinition("locationName", "Location Name", NodeParameterType.String), "location"),
            When(new NodeParameterDefinition("address", "Address", NodeParameterType.String), "location"),

            // reaction
            When(new NodeParameterDefinition("messageId", "Message ID", NodeParameterType.String, IsRequired: true), "reaction"),
            When(new NodeParameterDefinition("emoji", "Emoji", NodeParameterType.String, IsRequired: true, DefaultValue: "\U0001F44D"), "reaction"),

            new NodeParameterDefinition("apiVersion", "API Version", NodeParameterType.String, DefaultValue: "v22.0")
        ],
        ["communication"], "send", Color: "#25d366", Credentials: [CredentialTypeKey]);

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var credName = context.GetString("_credential", index)!;
        var accessToken = await context.GetCredentialAsync(CredentialTypeKey, credName, "accessToken")
            ?? throw new InvalidOperationException("whatsAppApi credential 'accessToken' eksik.");
        var phoneNumberId = await context.GetCredentialAsync(CredentialTypeKey, credName, "phoneNumberId")
            ?? throw new InvalidOperationException("whatsAppApi credential 'phoneNumberId' eksik.");

        var apiVersion = context.GetString("apiVersion", index);
        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            apiVersion = "v22.0";
        }

        var payload = BuildPayload(context, index);

        var url = $"https://graph.facebook.com/{apiVersion}/{phoneNumberId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await HttpHelper.Client(context).SendAsync(request, context.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
        return NodeItem.From(new JsonObject
        {
            ["statusCode"] = (int)response.StatusCode,
            ["response"] = HttpHelper.TryParseJson(body)
        });
    }

    /// <summary>Secilen <c>messageType</c>'a gore Graph API mesaj govdesini olusturur.</summary>
    internal static JsonObject BuildPayload(INodeExecutionContext context, int index)
    {
        var to = context.GetString("to", index);
        var messageType = context.GetString("messageType", index) ?? "text";

        var payload = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to
        };

        switch (messageType)
        {
            case "text":
                payload["type"] = "text";
                payload["text"] = new JsonObject
                {
                    ["preview_url"] = string.Equals(context.GetString("previewUrl", index), "true", StringComparison.OrdinalIgnoreCase),
                    ["body"] = context.GetString("text", index)
                };
                break;

            case "template":
                var template = new JsonObject
                {
                    ["name"] = context.GetString("templateName", index),
                    ["language"] = new JsonObject { ["code"] = context.GetString("languageCode", index) ?? "en_US" }
                };
                var bodyParams = ParseJson(context.GetString("bodyParams", index), "bodyParams") as JsonArray;
                if (bodyParams is { Count: > 0 })
                {
                    var parameters = new JsonArray();
                    foreach (var value in bodyParams)
                    {
                        parameters.Add(new JsonObject { ["type"] = "text", ["text"] = value?.ToString() });
                    }

                    template["components"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "body", ["parameters"] = parameters }
                    };
                }

                payload["type"] = "template";
                payload["template"] = template;
                break;

            case "image":
            case "document":
                var media = new JsonObject { ["link"] = context.GetString("mediaLink", index) };
                var caption = context.GetString("caption", index);
                if (!string.IsNullOrWhiteSpace(caption))
                {
                    media["caption"] = caption;
                }

                if (messageType == "document")
                {
                    var filename = context.GetString("filename", index);
                    if (!string.IsNullOrWhiteSpace(filename))
                    {
                        media["filename"] = filename;
                    }
                }

                payload["type"] = messageType;
                payload[messageType] = media;
                break;

            case "interactiveButtons":
                var buttonsArray = ParseJson(context.GetString("buttons", index), "buttons") as JsonArray
                    ?? throw new InvalidOperationException("buttons bir JSON dizisi olmali.");
                var buttons = new JsonArray();
                foreach (var btn in buttonsArray.OfType<JsonObject>())
                {
                    buttons.Add(new JsonObject
                    {
                        ["type"] = "reply",
                        ["reply"] = new JsonObject
                        {
                            ["id"] = btn["id"]?.ToString(),
                            ["title"] = btn["title"]?.ToString()
                        }
                    });
                }

                payload["type"] = "interactive";
                payload["interactive"] = new JsonObject
                {
                    ["type"] = "button",
                    ["body"] = new JsonObject { ["text"] = context.GetString("bodyText", index) },
                    ["action"] = new JsonObject { ["buttons"] = buttons }
                };
                break;

            case "interactiveList":
                var sections = ParseJson(context.GetString("sections", index), "sections") as JsonArray
                    ?? throw new InvalidOperationException("sections bir JSON dizisi olmali.");
                payload["type"] = "interactive";
                payload["interactive"] = new JsonObject
                {
                    ["type"] = "list",
                    ["body"] = new JsonObject { ["text"] = context.GetString("bodyText", index) },
                    ["action"] = new JsonObject
                    {
                        ["button"] = context.GetString("buttonText", index) ?? "Sec",
                        ["sections"] = sections.DeepClone()
                    }
                };
                break;

            case "location":
                payload["type"] = "location";
                payload["location"] = new JsonObject
                {
                    ["latitude"] = ToNumber(context.GetString("latitude", index)),
                    ["longitude"] = ToNumber(context.GetString("longitude", index)),
                    ["name"] = context.GetString("locationName", index),
                    ["address"] = context.GetString("address", index)
                };
                break;

            case "reaction":
                payload["type"] = "reaction";
                payload["reaction"] = new JsonObject
                {
                    ["message_id"] = context.GetString("messageId", index),
                    ["emoji"] = context.GetString("emoji", index)
                };
                break;

            default:
                throw new InvalidOperationException($"Desteklenmeyen messageType: {messageType}");
        }

        return payload;
    }

    private static JsonNode? ParseJson(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidOperationException($"'{field}' gecerli JSON degil: {exception.Message}");
        }
    }

    private static JsonNode? ToNumber(string? raw) =>
        double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? JsonValue.Create(value)
            : JsonValue.Create(raw);
}
