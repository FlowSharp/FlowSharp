using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Data;

/// <summary>
/// Hash, HMAC ve Base64 islemleri (yerlesik System.Security.Cryptography). Webhook imza
/// dogrulama, parmak izi uretme, kodlama gibi senaryolar icindir.
/// </summary>
public sealed class CryptoNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "transform.crypto",
        DisplayName: "Crypto",
        Category: NodeCategory.Data,
        Kind: NodeKind.Transform,
        Description: "Hash / HMAC / Base64 islemleri uygular.",
        Parameters:
        [
            new NodeParameterDefinition("operation", "Islem", NodeParameterType.Select, IsRequired: true,
                DefaultValue: "sha256",
                Options: ["md5", "sha1", "sha256", "sha512", "hmacSha256", "base64Encode", "base64Decode"]),
            new NodeParameterDefinition("value", "Deger", NodeParameterType.String, IsRequired: true,
                HelpText: "Ornek: {{$json.payload}}"),
            new NodeParameterDefinition("key", "Anahtar (HMAC)", NodeParameterType.String,
                HelpText: "Yalniz hmacSha256 icin gizli anahtar."),
            new NodeParameterDefinition("outputField", "Cikti alani", NodeParameterType.String, DefaultValue: "result")
        ],
        Tags: ["data", "crypto"],
        Icon: "sliders",
        Color: "#9b51e0");

    protected override Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var operation = context.GetString("operation", index) ?? "sha256";
        var value = context.GetString("value", index) ?? "";
        var field = context.GetString("outputField", index) ?? "result";
        var bytes = Encoding.UTF8.GetBytes(value);

        var result = operation switch
        {
            "md5" => ToHex(MD5.HashData(bytes)),
            "sha1" => ToHex(SHA1.HashData(bytes)),
            "sha256" => ToHex(SHA256.HashData(bytes)),
            "sha512" => ToHex(SHA512.HashData(bytes)),
            "hmacSha256" => ToHex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(context.GetString("key", index) ?? ""), bytes)),
            "base64Encode" => Convert.ToBase64String(bytes),
            "base64Decode" => DecodeBase64(value),
            _ => throw new InvalidOperationException($"Bilinmeyen islem: {operation}")
        };

        var output = (JsonObject)item.Json.DeepClone();
        output[field] = result;
        return Task.FromResult<NodeItem?>(NodeItem.From(output));
    }

    private static string ToHex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    private static string DecodeBase64(string value) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
