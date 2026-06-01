using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Database;

public abstract class DatabaseOperationNode : NodeType, IHasDynamicOptions
{
    public Task<IReadOnlyList<NodeParameterOption>> LoadOptionsAsync(INodeExecutionContext context, string parameterKey) =>
        parameterKey == "table"
            ? DatabaseNodeHelpers.ListTableOptionsAsync(context)
            : Task.FromResult<IReadOnlyList<NodeParameterOption>>([]);

    protected static IReadOnlyList<NodeParameterDefinition> ColumnsAndWhereParams =>
    [
        new NodeParameterDefinition("schema", "Schema", NodeParameterType.String),
        new NodeParameterDefinition("table", "Table", NodeParameterType.String,
            HelpText: "Optional override. If empty, uses upstream Table node.", DynamicOptions: true, InheritsUpstream: true),
        new NodeParameterDefinition("columnsJson", "Columns JSON", NodeParameterType.Json, DefaultValue: "{}",
            HelpText: "JSON object with column/value pairs."),
        new NodeParameterDefinition("where", "Where", NodeParameterType.Text,
            HelpText: "SQL WHERE clause without the WHERE keyword.")
    ];

    protected static async Task<(DatabaseConnectionState State, System.Data.Common.DbConnection Connection, string Table, string? Schema)?> OpenForTableAsync(
        INodeExecutionContext context)
    {
        var state = DatabaseNodeHelpers.ReadState(context);
        if (state is null)
        {
            return null;
        }

        var table = DatabaseNodeHelpers.ReadTable(context);
        if (string.IsNullOrWhiteSpace(table))
        {
            throw new InvalidOperationException("Table node veya Table parametresi gereklidir.");
        }

        var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return null;
        }

        return (state, connection, table, DatabaseNodeHelpers.ReadSchema(context, state));
    }
}

public sealed class DatabaseSelectNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.select",
        DisplayName: "Select",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Selects rows from the upstream database table.",
        Parameters:
        [
            new NodeParameterDefinition("schema", "Schema", NodeParameterType.String),
            new NodeParameterDefinition("table", "Table", NodeParameterType.String,
                HelpText: "Optional override. If empty, uses upstream Table node.", DynamicOptions: true, InheritsUpstream: true),
            new NodeParameterDefinition("columns", "Columns", NodeParameterType.String, DefaultValue: "*",
                HelpText: "Comma-separated column names or *."),
            new NodeParameterDefinition("where", "Where", NodeParameterType.Text),
            new NodeParameterDefinition("limit", "Limit", NodeParameterType.Number, DefaultValue: "0",
                HelpText: "0 means no limit.")
        ],
        Tags: ["database", "select"],
        Icon: "database",
        Color: "#0ea5e9",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var opened = await OpenForTableAsync(context);
        if (opened is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var connection = opened.Value.Connection;
        await using var command = connection.CreateCommand();
        var table = DatabaseNodeHelpers.QuotePath(opened.Value.State.Provider, opened.Value.Schema, opened.Value.Table);
        var columns = BuildColumns(opened.Value.State.Provider, context.GetString("columns") ?? "*");
        var limit = context.GetInt("limit");
        var sql = $"SELECT {columns} FROM {table}{DatabaseNodeHelpers.BuildWhere(context.GetString("where"))}";

        if (limit > 0)
        {
            sql = opened.Value.State.Provider == DatabaseProvider.SqlServer
                ? sql.Replace("SELECT ", $"SELECT TOP ({limit}) ", StringComparison.Ordinal)
                : $"{sql} LIMIT {limit}";
        }

        command.CommandText = $"{sql};";
        return await DatabaseNodeHelpers.ReadRowsAsync(command, context.CancellationToken);
    }

    private static string BuildColumns(DatabaseProvider provider, string columns)
    {
        if (string.IsNullOrWhiteSpace(columns) || columns.Trim() == "*")
        {
            return "*";
        }

        return string.Join(", ", columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(column => DatabaseNodeHelpers.QuoteIdentifier(provider, column)));
    }
}

public sealed class DatabaseInsertNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.insert",
        DisplayName: "Insert",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Inserts a row into the upstream database table.",
        Parameters: ColumnsAndWhereParams.Take(3).ToArray(),
        Tags: ["database", "insert"],
        Icon: "database",
        Color: "#22c55e",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var opened = await OpenForTableAsync(context);
        if (opened is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var connection = opened.Value.Connection;
        var values = DatabaseNodeHelpers.ReadColumns(context);
        if (values.Count == 0)
        {
            return NodeExecutionResult.Failure("Insert icin Columns JSON en az bir alan icermelidir.");
        }

        await using var command = connection.CreateCommand();
        var columns = values.Keys.Select(key => DatabaseNodeHelpers.QuoteIdentifier(opened.Value.State.Provider, key)).ToArray();
        var parameters = values.Select((pair, index) =>
        {
            var name = $"@p{index}";
            DatabaseNodeHelpers.AddParameter(command, name, pair.Value);
            return name;
        }).ToArray();

        command.CommandText = $"INSERT INTO {DatabaseNodeHelpers.QuotePath(opened.Value.State.Provider, opened.Value.Schema, opened.Value.Table)} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", parameters)});";
        var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
        return DatabaseNodeHelpers.AffectedRows(affected);
    }
}

public sealed class DatabaseUpdateNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.update",
        DisplayName: "Update",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Updates rows in the upstream database table.",
        Parameters: ColumnsAndWhereParams,
        Tags: ["database", "update"],
        Icon: "database",
        Color: "#f59e0b",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var opened = await OpenForTableAsync(context);
        if (opened is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var connection = opened.Value.Connection;
        var values = DatabaseNodeHelpers.ReadColumns(context);
        if (values.Count == 0)
        {
            return NodeExecutionResult.Failure("Update icin Columns JSON en az bir alan icermelidir.");
        }

        await using var command = connection.CreateCommand();
        var setClauses = values.Select((pair, index) =>
        {
            var name = $"@p{index}";
            DatabaseNodeHelpers.AddParameter(command, name, pair.Value);
            return $"{DatabaseNodeHelpers.QuoteIdentifier(opened.Value.State.Provider, pair.Key)} = {name}";
        });

        command.CommandText = $"UPDATE {DatabaseNodeHelpers.QuotePath(opened.Value.State.Provider, opened.Value.Schema, opened.Value.Table)} SET {string.Join(", ", setClauses)}{DatabaseNodeHelpers.BuildWhere(context.GetString("where"))};";
        var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
        return DatabaseNodeHelpers.AffectedRows(affected);
    }
}

public sealed class DatabaseUpsertNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.upsert",
        DisplayName: "Insert or Update",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Inserts a row or updates it when key columns already exist.",
        Parameters:
        [
            new NodeParameterDefinition("schema", "Schema", NodeParameterType.String),
            new NodeParameterDefinition("table", "Table", NodeParameterType.String,
                HelpText: "Optional override. If empty, uses upstream Table node.", DynamicOptions: true, InheritsUpstream: true),
            new NodeParameterDefinition("keyColumns", "Key Columns", NodeParameterType.String, IsRequired: true,
                HelpText: "Comma-separated key columns, for example id or tenantId,email."),
            new NodeParameterDefinition("columnsJson", "Columns JSON", NodeParameterType.Json, DefaultValue: "{}",
                HelpText: "JSON object with all insert/update values, including key columns.")
        ],
        Tags: ["database", "upsert"],
        Icon: "database",
        Color: "#14b8a6",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var opened = await OpenForTableAsync(context);
        if (opened is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var connection = opened.Value.Connection;
        var values = DatabaseNodeHelpers.ReadColumns(context);
        var keyColumns = (context.GetString("keyColumns") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (values.Count == 0 || keyColumns.Length == 0)
        {
            return NodeExecutionResult.Failure("Upsert icin Key Columns ve Columns JSON gereklidir.");
        }

        var missingKeys = keyColumns.Where(key => !values.ContainsKey(key)).ToArray();
        if (missingKeys.Length > 0)
        {
            return NodeExecutionResult.Failure($"Columns JSON key column degerlerini icermelidir: {string.Join(", ", missingKeys)}");
        }

        await using var command = connection.CreateCommand();
        var provider = opened.Value.State.Provider;
        var table = DatabaseNodeHelpers.QuotePath(provider, opened.Value.Schema, opened.Value.Table);
        var columns = values.Keys.ToArray();
        var quotedColumns = columns.Select(column => DatabaseNodeHelpers.QuoteIdentifier(provider, column)).ToArray();
        var parameterNames = columns.Select((column, index) =>
        {
            var name = $"@p{index}";
            DatabaseNodeHelpers.AddParameter(command, name, values[column]);
            return name;
        }).ToArray();

        var updateColumns = columns.Where(column => !keyColumns.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (updateColumns.Length == 0)
        {
            return NodeExecutionResult.Failure("Upsert icin key kolonlari disinda en az bir guncellenecek kolon gereklidir.");
        }

        command.CommandText = provider switch
        {
            DatabaseProvider.MySql => BuildMySqlUpsert(provider, table, quotedColumns, parameterNames, updateColumns),
            DatabaseProvider.SqlServer => BuildSqlServerUpsert(provider, table, columns, parameterNames, keyColumns, updateColumns),
            _ => BuildPostgresUpsert(provider, table, quotedColumns, parameterNames, keyColumns, updateColumns)
        };

        var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
        return DatabaseNodeHelpers.AffectedRows(affected);
    }

    private static string BuildPostgresUpsert(
        DatabaseProvider provider,
        string table,
        IReadOnlyList<string> quotedColumns,
        IReadOnlyList<string> parameterNames,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> updateColumns)
    {
        var conflict = string.Join(", ", keyColumns.Select(column => DatabaseNodeHelpers.QuoteIdentifier(provider, column)));
        var update = string.Join(", ", updateColumns.Select(column =>
            $"{DatabaseNodeHelpers.QuoteIdentifier(provider, column)} = EXCLUDED.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)}"));
        return $"INSERT INTO {table} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", parameterNames)}) ON CONFLICT ({conflict}) DO UPDATE SET {update};";
    }

    private static string BuildMySqlUpsert(
        DatabaseProvider provider,
        string table,
        IReadOnlyList<string> quotedColumns,
        IReadOnlyList<string> parameterNames,
        IReadOnlyList<string> updateColumns)
    {
        var update = string.Join(", ", updateColumns.Select(column =>
            $"{DatabaseNodeHelpers.QuoteIdentifier(provider, column)} = VALUES({DatabaseNodeHelpers.QuoteIdentifier(provider, column)})"));
        return $"INSERT INTO {table} ({string.Join(", ", quotedColumns)}) VALUES ({string.Join(", ", parameterNames)}) ON DUPLICATE KEY UPDATE {update};";
    }

    private static string BuildSqlServerUpsert(
        DatabaseProvider provider,
        string table,
        IReadOnlyList<string> columns,
        IReadOnlyList<string> parameterNames,
        IReadOnlyList<string> keyColumns,
        IReadOnlyList<string> updateColumns)
    {
        var sourceColumns = columns.Select((column, index) =>
            $"{parameterNames[index]} AS {DatabaseNodeHelpers.QuoteIdentifier(provider, column)}");
        var on = string.Join(" AND ", keyColumns.Select(column =>
            $"target.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)} = source.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)}"));
        var update = string.Join(", ", updateColumns.Select(column =>
            $"target.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)} = source.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)}"));
        var insertColumns = string.Join(", ", columns.Select(column => DatabaseNodeHelpers.QuoteIdentifier(provider, column)));
        var insertValues = string.Join(", ", columns.Select(column => $"source.{DatabaseNodeHelpers.QuoteIdentifier(provider, column)}"));

        return $"""
MERGE {table} AS target
USING (SELECT {string.Join(", ", sourceColumns)}) AS source
ON {on}
WHEN MATCHED THEN UPDATE SET {update}
WHEN NOT MATCHED THEN INSERT ({insertColumns}) VALUES ({insertValues});
""";
    }
}

public sealed class DatabaseDeleteNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.delete",
        DisplayName: "Delete",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Deletes rows from the upstream database table.",
        Parameters:
        [
            new NodeParameterDefinition("schema", "Schema", NodeParameterType.String),
            new NodeParameterDefinition("table", "Table", NodeParameterType.String,
                HelpText: "Optional override. If empty, uses upstream Table node.", DynamicOptions: true, InheritsUpstream: true),
            new NodeParameterDefinition("where", "Where", NodeParameterType.Text)
        ],
        Tags: ["database", "delete"],
        Icon: "database",
        Color: "#ef4444",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var opened = await OpenForTableAsync(context);
        if (opened is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var connection = opened.Value.Connection;
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {DatabaseNodeHelpers.QuotePath(opened.Value.State.Provider, opened.Value.Schema, opened.Value.Table)}{DatabaseNodeHelpers.BuildWhere(context.GetString("where"))};";
        var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
        return DatabaseNodeHelpers.AffectedRows(affected);
    }
}

public sealed class DatabaseExecuteQueryNode : DatabaseOperationNode
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.executeQuery",
        DisplayName: "Execute Query",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Executes a raw SQL query using the upstream database connection.",
        Parameters:
        [
            new NodeParameterDefinition("query", "SQL Query", NodeParameterType.Code, IsRequired: true, DefaultValue: "SELECT 1 AS ok;"),
            new NodeParameterDefinition("returnsRows", "Returns Rows", NodeParameterType.Boolean, DefaultValue: "true")
        ],
        Tags: ["database", "query"],
        Icon: "database",
        Color: "#6366f1",
        SubCategory: "Operations");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var state = DatabaseNodeHelpers.ReadState(context);
        if (state is null)
        {
            return NodeExecutionResult.Failure("Execute Query node bir database connection node'undan sonra calismalidir.");
        }

        await using var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return NodeExecutionResult.Failure("Database connection bulunamadi.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = context.GetString("query") ?? "";
        if (string.IsNullOrWhiteSpace(command.CommandText))
        {
            return NodeExecutionResult.Failure("SQL Query bos olamaz.");
        }

        if (context.GetBoolean("returnsRows", defaultValue: true))
        {
            return await DatabaseNodeHelpers.ReadRowsAsync(command, context.CancellationToken);
        }

        var affected = await command.ExecuteNonQueryAsync(context.CancellationToken);
        return DatabaseNodeHelpers.AffectedRows(affected);
    }
}
