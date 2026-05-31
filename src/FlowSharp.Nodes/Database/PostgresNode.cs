using System.Text.Json.Nodes;
using Npgsql;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Database;

/// <summary>
/// Gercek PostgreSQL sorgusu calistirir (Npgsql). Baglanti bilgisi "postgres" tipli
/// credential'dan gelir: "connectionString" alani ya da host/port/database/user/password alanlari.
/// </summary>
public sealed class PostgresNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "postgres.query",
        DisplayName: "Postgres",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Bir PostgreSQL sorgusu calistirir.",
        Parameters:
        [
            new NodeParameterDefinition("_credential", "Credential", NodeParameterType.Credential, IsRequired: true,
                HelpText: "postgres tipli credential adi"),
            new NodeParameterDefinition("operation", "Operation", NodeParameterType.Select, DefaultValue: "select",
                Options: ["select", "execute"]),
            new NodeParameterDefinition("query", "SQL Query", NodeParameterType.Code, IsRequired: true,
                DefaultValue: "SELECT 1 AS ok;")
        ],
        Tags: ["database"],
        Icon: "database",
        Color: "#336791",
        Credentials: ["postgres"]);

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var connectionString = await ResolveConnectionStringAsync(context);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return NodeExecutionResult.Failure("Postgres credential bulunamadi veya baglanti bilgisi eksik.");
        }

        var query = context.GetString("query") ?? "";
        var operation = context.GetString("operation") ?? "select";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(context.CancellationToken);
        await using var command = new NpgsqlCommand(query, connection);

        if (operation == "execute")
        {
            var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
            return NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["affectedRows"] = affected }));
        }

        var items = new List<NodeItem>();
        await using var reader = await command.ExecuteReaderAsync(context.CancellationToken);
        while (await reader.ReadAsync(context.CancellationToken))
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = ToJson(reader.IsDBNull(i) ? null : reader.GetValue(i));
            }
            items.Add(NodeItem.From(row));
        }

        return NodeExecutionResult.Single(items);
    }

    private async Task<string?> ResolveConnectionStringAsync(INodeExecutionContext context)
    {
        var credName = context.GetString("_credential");
        if (string.IsNullOrWhiteSpace(credName))
        {
            return null;
        }

        var conn = await context.GetCredentialAsync("postgres", credName, "connectionString");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            return conn;
        }

        var host = await context.GetCredentialAsync("postgres", credName, "host");
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = int.TryParse(await context.GetCredentialAsync("postgres", credName, "port"), out var port) ? port : 5432,
            Database = await context.GetCredentialAsync("postgres", credName, "database"),
            Username = await context.GetCredentialAsync("postgres", credName, "user"),
            Password = await context.GetCredentialAsync("postgres", credName, "password")
        };
        return builder.ConnectionString;
    }

    private static JsonNode? ToJson(object? value) => value switch
    {
        null => null,
        bool b => b,
        int i => i,
        long l => l,
        short s => s,
        decimal d => d,
        double db => db,
        float f => f,
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToString("O"),
        Guid g => g.ToString(),
        _ => value.ToString()
    };
}
