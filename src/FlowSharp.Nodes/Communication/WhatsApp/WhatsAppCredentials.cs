using FlowSharp.Domain.Credentials;
using F = FlowSharp.Domain.Credentials.CredentialFieldSchema;
using CT = FlowSharp.Domain.Credentials.CredentialFieldType;

namespace FlowSharp.Nodes.Communication.WhatsApp;

/// <summary>
/// WhatsApp Cloud API node'larinin paylastigi credential tanimi.
/// Tip "whatsAppApi": accessToken (Bearer) + phoneNumberId (gonderen numara kimligi).
/// </summary>
internal static class WhatsAppCredentials
{
    public const string TypeKey = "whatsAppApi";

    public static CredentialSchema Schema => new(TypeKey, "WhatsApp Cloud API",
    [
        new F("accessToken", "Access Token", CT.Secret, IsRequired: true,
            HelpText: "Meta uygulamasinin kalici/gecici erisim token'i."),
        new F("phoneNumberId", "Phone Number ID", CT.String, IsRequired: true,
            HelpText: "WhatsApp gonderen numara kimligi (Graph API).")
    ]);
}
