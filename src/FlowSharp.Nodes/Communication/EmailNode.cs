using System.Text.Json.Nodes;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Communication;

/// <summary>
/// SMTP uzerinden gercek e-posta gonderir (MailKit). "smtp" tipli credential alanlari:
/// host, port, user, password, secure (true/false). Implicit SSL (465), STARTTLS (587)
/// ve sifresiz baglanti desteklenir.
/// </summary>
public sealed class EmailNode : PerItemNodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "email.send",
        DisplayName: "Send Email",
        Category: NodeCategory.Communication,
        Kind: NodeKind.Action,
        Description: "SMTP ile e-posta gonderir.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "smtp tipli credential adi"),
            new NodeParameterDefinition("from", "From", NodeParameterType.String, IsRequired: true),
            new NodeParameterDefinition("to", "To", NodeParameterType.String, IsRequired: true),
            new NodeParameterDefinition("subject", "Subject", NodeParameterType.String),
            new NodeParameterDefinition("body", "Body", NodeParameterType.Text),
            new NodeParameterDefinition("isHtml", "HTML?", NodeParameterType.Boolean, DefaultValue: "false")
        ],
        Tags: ["communication"],
        Icon: "mail",
        Color: "#2f80ed",
        Credentials: ["smtp"]);

    protected override async Task<NodeItem?> ProcessItemAsync(INodeExecutionContext context, NodeItem item, int index)
    {
        var credName = context.GetString("_credential", index)
            ?? throw new InvalidOperationException("Email node icin credential gerekli.");

        var host = await context.GetCredentialAsync("smtp", credName, "host")
            ?? throw new InvalidOperationException("smtp credential 'host' eksik.");
        var port = int.TryParse(await context.GetCredentialAsync("smtp", credName, "port"), out var p) ? p : 587;
        var user = await context.GetCredentialAsync("smtp", credName, "user");
        var password = await context.GetCredentialAsync("smtp", credName, "password");
        var secure = (await context.GetCredentialAsync("smtp", credName, "secure")) != "false";

        // 465 => implicit SSL (SslOnConnect), 587/diger => STARTTLS, secure=false => hicbiri.
        var socketOptions = secure
            ? (port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls)
            : SecureSocketOptions.None;

        var to = context.GetString("to", index)!;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(context.GetString("from", index)!));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = context.GetString("subject", index) ?? "";

        var bodyText = context.GetString("body", index) ?? "";
        message.Body = context.GetBoolean("isHtml", index)
            ? new BodyBuilder { HtmlBody = bodyText }.ToMessageBody()
            : new TextPart("plain") { Text = bodyText };

        try
        {
            using var client = new SmtpClient { Timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds };
            await client.ConnectAsync(host, port, socketOptions, context.CancellationToken);
            if (!string.IsNullOrEmpty(user))
            {
                await client.AuthenticateAsync(user, password ?? "", context.CancellationToken);
            }
            await client.SendAsync(message, context.CancellationToken);
            await client.DisconnectAsync(true, context.CancellationToken);
        }
        catch (Exception ex)
        {
            return NodeItem.From(new JsonObject { ["error"] = ex.Message });
        }

        return NodeItem.From(new JsonObject { ["sent"] = true, ["to"] = to });
    }
}
