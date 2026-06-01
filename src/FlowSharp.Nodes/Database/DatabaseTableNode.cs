using System.Text.Json.Nodes;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Database;

public sealed class DatabaseTableNode : NodeType, IHasDynamicOptions
{
    public Task<IReadOnlyList<NodeParameterOption>> LoadOptionsAsync(INodeExecutionContext context, string parameterKey) =>
        parameterKey == "table"
            ? DatabaseNodeHelpers.ListTableOptionsAsync(context)
            : Task.FromResult<IReadOnlyList<NodeParameterOption>>([]);

    public override NodeDefinition Definition { get; } = new(
        Key: "db.table",
        DisplayName: "Table",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Selects a table from the upstream database connection and loads column metadata.",
        Parameters:
        [
            new NodeParameterDefinition("schema", "Schema", NodeParameterType.String,
                HelpText: "Optional schema override."),
            new NodeParameterDefinition("table", "Table", NodeParameterType.String,
                HelpText: "Table name. Leave empty to list available tables.", DynamicOptions: true)
        ],
        Tags: ["database", "schema", "table"],
        Icon: "table",
        Color: "#64748b",
        SubCategory: "Schema");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var state = DatabaseNodeHelpers.ReadState(context);
        if (state is null)
        {
            return NodeExecutionResult.Failure("Table node bir database connection node'undan sonra calismalidir.");
        }

        await using var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return NodeExecutionResult.Failure("Database credential bulunamadi veya baglanti bilgisi eksik.");
        }

        var schema = DatabaseNodeHelpers.ReadSchema(context, state);
        var table = context.GetString("table");

        if (string.IsNullOrWhiteSpace(table))
        {
            return await ListTablesAsync(connection, state, schema, context);
        }

        var columns = await LoadColumnsAsync(connection, state.Provider, schema, table, context.CancellationToken);
        var output = DatabaseNodeHelpers.ToJson(state);
        output["schema"] = schema;
        output["table"] = table;
        output["columns"] = new JsonArray(columns.Select(column => (JsonNode)new JsonObject
        {
            ["name"] = column.Name,
            ["dataType"] = column.DataType,
            ["nullable"] = column.Nullable
        }).ToArray());

        // Onizleme: secilen tablonun ilk satirlari (downstream node'lar yine state'i [0]. item'dan okur).
        output["preview"] = await LoadPreviewAsync(connection, state.Provider, schema, table, context.CancellationToken);

        return NodeExecutionResult.Single(NodeItem.From(output));
    }

    private static async Task<NodeExecutionResult> ListTablesAsync(
        System.Data.Common.DbConnection connection,
        DatabaseConnectionState state,
        string? schema,
        INodeExecutionContext context)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = state.Provider switch
        {
            DatabaseProvider.SqlServer => string.IsNullOrWhiteSpace(schema)
                ? "SELECT TABLE_SCHEMA AS schema_name, TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME"
                : "SELECT TABLE_SCHEMA AS schema_name, TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = @schema ORDER BY TABLE_SCHEMA, TABLE_NAME",
            DatabaseProvider.MySql => "SELECT TABLE_SCHEMA AS schema_name, TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
            _ => string.IsNullOrWhiteSpace(schema)
                ? "SELECT table_schema AS schema_name, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_schema, table_name"
                : "SELECT table_schema AS schema_name, table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = @schema ORDER BY table_schema, table_name"
        };

        if (!string.IsNullOrWhiteSpace(schema) && state.Provider != DatabaseProvider.MySql)
        {
            DatabaseNodeHelpers.AddParameter(command, "@schema", schema);
        }

        return await DatabaseNodeHelpers.ReadRowsAsync(command, context.CancellationToken);
    }

    private static async Task<IReadOnlyList<ColumnInfo>> LoadColumnsAsync(
        System.Data.Common.DbConnection connection,
        DatabaseProvider provider,
        string? schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = provider switch
        {
            DatabaseProvider.SqlServer => "SELECT COLUMN_NAME AS column_name, DATA_TYPE AS data_type, IS_NULLABLE AS is_nullable FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND (@schema IS NULL OR TABLE_SCHEMA = @schema) ORDER BY ORDINAL_POSITION",
            DatabaseProvider.MySql => "SELECT COLUMN_NAME AS column_name, DATA_TYPE AS data_type, IS_NULLABLE AS is_nullable FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            // Postgres: @schema null oldugunda parametre tipi belirlenemez (42P08); ::text ile cast edilir.
            _ => "SELECT column_name, data_type, is_nullable FROM information_schema.columns WHERE table_name = @table AND (@schema::text IS NULL OR table_schema = @schema::text) ORDER BY ordinal_position"
        };

        DatabaseNodeHelpers.AddParameter(command, "@table", table);
        if (provider != DatabaseProvider.MySql)
        {
            DatabaseNodeHelpers.AddParameter(command, "@schema", string.IsNullOrWhiteSpace(schema) ? null : schema);
        }

        var columns = new List<ColumnInfo>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new ColumnInfo(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase)));
        }

        return columns;
    }

    private const int PreviewLimit = 50;

    private static async Task<JsonArray> LoadPreviewAsync(
        System.Data.Common.DbConnection connection,
        DatabaseProvider provider,
        string? schema,
        string table,
        CancellationToken cancellationToken)
    {
        var rows = new JsonArray();
        try
        {
            await using var command = connection.CreateCommand();
            var quoted = DatabaseNodeHelpers.QuotePath(provider, schema, table);
            command.CommandText = provider == DatabaseProvider.SqlServer
                ? $"SELECT TOP ({PreviewLimit}) * FROM {quoted};"
                : $"SELECT * FROM {quoted} LIMIT {PreviewLimit};";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new JsonObject();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = DatabaseNodeHelpers.ToJson(reader.IsDBNull(i) ? null : reader.GetValue(i));
                }

                rows.Add(row);
            }
        }
        catch
        {
            // Onizleme best-effort; tabloya erisilemezse sessizce bos doner.
        }

        return rows;
    }

    private sealed record ColumnInfo(string Name, string DataType, bool Nullable);
}
