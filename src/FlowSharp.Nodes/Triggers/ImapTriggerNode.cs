using System.Text.Json.Nodes;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Triggers;

/// <summary>
/// IMAP gelen kutusunu izleyen tetikleyici. SchedulerService "pollCron" periyodunda
/// workflow'u kuyruga ekler; node calistiginda okunmamis (UNSEEN) mailleri ceker ve
/// her birini bir cikis item'i olarak dondurur. "imap" tipli credential alanlari:
/// host, port, user, password, secure (true/false).
/// </summary>
public sealed class ImapTriggerNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "email.imap.trigger",
        DisplayName: "Email Trigger (IMAP)",
        Category: NodeCategory.Trigger,
        Kind: NodeKind.Trigger,
        Description: "IMAP gelen kutusuna yeni e-posta dustugunde workflow'u baslatir.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "imap tipli credential adi"),
            new NodeParameterDefinition("folder", "Klasor", NodeParameterType.String, DefaultValue: "INBOX"),
            new NodeParameterDefinition("pollCron", "Poll (Cron)", NodeParameterType.String, IsRequired: true,
                DefaultValue: "*/5 * * * *", HelpText: "Ornek: */5 * * * * (her 5 dakikada bir kontrol)"),
            new NodeParameterDefinition("markSeen", "Okundu isaretle?", NodeParameterType.Boolean, DefaultValue: "true"),
            new NodeParameterDefinition("limit", "Maks. mail", NodeParameterType.Number, DefaultValue: "20")
        ],
        Tags: ["trigger"],
        Icon: "mail",
        Color: "#7d7d87",
        Credentials: ["imap"]);

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var credName = context.GetString("_credential")
            ?? throw new InvalidOperationException("IMAP trigger icin credential gerekli.");

        var host = await context.GetCredentialAsync("imap", credName, "host")
            ?? throw new InvalidOperationException("imap credential 'host' eksik.");
        var port = int.TryParse(await context.GetCredentialAsync("imap", credName, "port"), out var p) ? p : 993;
        var user = await context.GetCredentialAsync("imap", credName, "user");
        var password = await context.GetCredentialAsync("imap", credName, "password");
        var secure = (await context.GetCredentialAsync("imap", credName, "secure")) != "false";

        var folderName = context.GetString("folder") ?? "INBOX";
        var markSeen = context.GetBoolean("markSeen", defaultValue: true);
        var limit = Math.Max(1, context.GetInt("limit", defaultValue: 20));

        var socketOptions = secure
            ? (port == 143 ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect)
            : SecureSocketOptions.None;

        var token = context.CancellationToken;
        var output = new List<NodeItem>();

        using var client = new ImapClient { Timeout = (int)TimeSpan.FromSeconds(30).TotalMilliseconds };
        await client.ConnectAsync(host, port, socketOptions, token);
        if (!string.IsNullOrEmpty(user))
        {
            await client.AuthenticateAsync(user, password ?? "", token);
        }

        var folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
            ? client.Inbox
            : await client.GetFolderAsync(folderName, token);
        await folder.OpenAsync(FolderAccess.ReadWrite, token);

        var uids = await folder.SearchAsync(SearchQuery.NotSeen, token);
        foreach (var uid in uids.Take(limit))
        {
            token.ThrowIfCancellationRequested();
            var message = await folder.GetMessageAsync(uid, token);

            output.Add(NodeItem.From(new JsonObject
            {
                ["uid"] = uid.Id,
                ["messageId"] = message.MessageId,
                ["from"] = message.From.ToString(),
                ["to"] = message.To.ToString(),
                ["subject"] = message.Subject ?? "",
                ["date"] = message.Date.ToString("O"),
                ["text"] = message.TextBody ?? "",
                ["html"] = message.HtmlBody ?? ""
            }));

            if (markSeen)
            {
                await folder.AddFlagsAsync(uid, MessageFlags.Seen, true, token);
            }
        }

        await client.DisconnectAsync(true, token);

        context.Log($"IMAP trigger: {output.Count} yeni mail alindi ({folderName}).");
        return NodeExecutionResult.Single(output);
    }
}
