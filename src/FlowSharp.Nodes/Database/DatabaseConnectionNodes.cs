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
