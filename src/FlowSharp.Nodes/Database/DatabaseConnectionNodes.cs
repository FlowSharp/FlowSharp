using Microsoft.Extensions.Hosting;
using FlowSharp.Application.Credentials;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Credentials;
using FlowSharp.Domain.Nodes;
using FlowSharp.Nodes.Credentials;

namespace FlowSharp.Nodes.Database;

public abstract class DatabaseConnectionNode : NodeType, IProvidesCredentials
{
    protected abstract DatabaseProvider Provider { get; }

    protected abstract string CredentialType { get; }

    protected abstract string Display { get; }

    protected abstract string Color { get; }

    public IEnumerable<CredentialSchema> CredentialSchemas =>
    [
        new CredentialSchema(CredentialType, Display,
            Provider == DatabaseProvider.SqlServer ? CredentialFields.SqlServer() : CredentialFields.Database())
    ];

    public override NodeDefinition Definition => new(
        Key: $"db.{CredentialType}.connection",
        DisplayName: $"{Display} Connection",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: $"Creates a reusable {Display} connection context for downstream database nodes.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: $"Create or select an existing {Display} credential."),
            new NodeParameterDefinition("database", "Database", NodeParameterType.String,
                HelpText: "Baglanilacak veritabani adi. Bos birakilirsa sunucunun varsayilan veritabani kullanilir."),
            new NodeParameterDefinition("schema", "Schema", NodeParameterType.String,
                HelpText: "Optional default schema, for example public or dbo."),
            new NodeParameterDefinition("testConnection", "Test Connection", NodeParameterType.Boolean, DefaultValue: "true")
        ],
        Tags: ["database", "connection"],
        Icon: "database",
        Color: Color,
        Credentials: [CredentialType],
        SubCategory: "Connections");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var credentialName = context.GetString("_credential");
        if (string.IsNullOrWhiteSpace(credentialName))
        {
            return NodeExecutionResult.Failure($"{Display} credential secilmelidir.");
        }

        var state = new DatabaseConnectionState(
            Provider,
            CredentialType,
            credentialName,
            context.GetString("database"),
            context.GetString("schema"));

        if (context.GetBoolean("testConnection", defaultValue: true))
        {
            await using var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
            if (connection is null)
            {
                return NodeExecutionResult.Failure($"{Display} credential bulunamadi veya baglanti bilgisi eksik.");
            }
        }

        return NodeExecutionResult.Single(NodeItem.From(DatabaseNodeHelpers.ToJson(state)));
    }
}

public sealed class PostgresConnectionNode : DatabaseConnectionNode
{
    protected override DatabaseProvider Provider => DatabaseProvider.Postgres;
    protected override string CredentialType => "postgres";
    protected override string Display => "Postgres";
    protected override string Color => "#336791";
}

public sealed class SqlServerConnectionNode : DatabaseConnectionNode
{
    protected override DatabaseProvider Provider => DatabaseProvider.SqlServer;
    protected override string CredentialType => "sqlServer";
    protected override string Display => "SQL Server";
    protected override string Color => "#a91d22";
}

public sealed class MySqlConnectionNode : DatabaseConnectionNode
{
    protected override DatabaseProvider Provider => DatabaseProvider.MySql;
    protected override string CredentialType => "mysql";
    protected override string Display => "MySQL";
    protected override string Color => "#00758f";
}

/// <summary>
/// Yerel, dosya tabanli SQLite baglantisi. Diger connection node'larin aksine credential degil,
/// workflow'a izole bir dosya kullanir: App_Data/{workflowId}/{database}.db. Boylece uzak DB/ag
/// maliyeti olmadan, downstream db.* node'lariyla tam (cok tablolu) CRUD yapilabilir.
/// </summary>
public sealed class SqliteConnectionNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.sqlite.connection",
        DisplayName: "SQLite Connection",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Yerel bir SQLite .db dosyasi icin yeniden kullanilabilir baglanti baglami olusturur (App_Data/{workflowId}/{database}.db).",
        Parameters:
        [
            new NodeParameterDefinition("database", "Database File", NodeParameterType.String, IsRequired: true,
                DefaultValue: "data",
                HelpText: "Dosya adi (uzantisiz). App_Data/{workflowId}/{ad}.db olarak olusturulur."),
            new NodeParameterDefinition("testConnection", "Test Connection", NodeParameterType.Boolean, DefaultValue: "true")
        ],
        Tags: ["database", "connection", "sqlite"],
        Icon: "database",
        Color: "#0f80cc",
        SubCategory: "Connections");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var name = SafeFileName(context.GetString("database"));
        if (name is null)
        {
            return NodeExecutionResult.Failure("Gecerli bir SQLite dosya adi girilmelidir (harf/rakam/-/_).");
        }

        var environment = (IHostEnvironment)context.Services.GetService(typeof(IHostEnvironment))!;
        var scope = context.WorkflowId?.ToString("N") ?? "global";
        var directory = Path.Combine(environment.ContentRootPath, "App_Data", scope);
        Directory.CreateDirectory(directory);
        var dataSource = Path.Combine(directory, name + ".db");

        var state = new DatabaseConnectionState(
            DatabaseProvider.Sqlite,
            CredentialType: "",
            CredentialName: "",
            Database: name,
            Schema: null,
            DataSource: dataSource);

        if (context.GetBoolean("testConnection", defaultValue: true))
        {
            await using var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
            if (connection is null)
            {
                return NodeExecutionResult.Failure("SQLite dosyasi acilamadi.");
            }
        }

        return NodeExecutionResult.Single(NodeItem.From(DatabaseNodeHelpers.ToJson(state)));
    }

    private static string? SafeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(raw.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }
}
