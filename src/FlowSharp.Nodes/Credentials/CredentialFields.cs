using FlowSharp.Domain.Credentials;
using F = FlowSharp.Domain.Credentials.CredentialFieldSchema;
using CT = FlowSharp.Domain.Credentials.CredentialFieldType;

namespace FlowSharp.Nodes.Credentials;

/// <summary>
/// Node'larin kendi credential semalarini olustururken kullanabilecegi ortak alan kaliplari.
/// Merkezi bir kayit DEGILDIR; sadece tekrari onleyen yardimci (node istedigini secer/ozellestirir).
/// </summary>
public static class CredentialFields
{
    public static IReadOnlyList<F> ApiKey() =>
        [new F("apiKey", "API Key", CT.Secret, IsRequired: true)];

    public static IReadOnlyList<F> Database() =>
    [
        new F("host", "Host", CT.String, IsRequired: true, DefaultValue: "localhost"),
        new F("port", "Port", CT.Number),
        new F("user", "User", CT.String),
        new F("password", "Password", CT.Secret),
        new F("ssl", "SSL", CT.Boolean, DefaultValue: "false")
    ];

    // SQL Server, kullanici/parola yerine Windows kimligi (Integrated Security) kullanabilir.
    public static IReadOnlyList<F> SqlServer() =>
    [
        ..Database(),
        new F("integratedSecurity", "Integrated Security (Windows)", CT.Boolean, DefaultValue: "false")
    ];

    public static IReadOnlyList<F> Mail() =>
    [
        new F("host", "Host", CT.String, IsRequired: true),
        new F("port", "Port", CT.Number),
        new F("user", "User", CT.String),
        new F("password", "Password", CT.Secret),
        new F("secure", "Secure (SSL/TLS)", CT.Boolean, DefaultValue: "true")
    ];

    public static IReadOnlyList<F> Token() =>
        [new F("token", "Token", CT.Secret, IsRequired: true)];
}
