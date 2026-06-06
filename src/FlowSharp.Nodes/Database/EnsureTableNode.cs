using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FlowSharp.Application.Nodes;
using FlowSharp.Domain.Nodes;

namespace FlowSharp.Nodes.Database;

/// <summary>
/// Bir database connection node'undan sonra calisir ve UI'da tanimlanan kolonlardan
/// <c>CREATE TABLE IF NOT EXISTS</c> uretip calistirir. Boylece tablolar (ve birden cok tablo)
/// kod degisikligi olmadan, dinamik olarak UI'dan olusturulur. Tum saglayicilarda calisir;
/// ozellikle yerel SQLite ile workflow'a gomulu sema kurmak icindir.
/// </summary>
public sealed partial class EnsureTableNode : NodeType
{
    public override NodeDefinition Definition { get; } = new(
        Key: "db.ensureTable",
        DisplayName: "Ensure Table",
        Category: NodeCategory.Database,
        Kind: NodeKind.Action,
        Description: "Tablo yoksa, verilen kolon tanimlarindan olusturur (CREATE TABLE IF NOT EXISTS).",
        Parameters:
        [
            new NodeParameterDefinition("table", "Table", NodeParameterType.String, IsRequired: true,
                HelpText: "Olusturulacak/dogrulanacak tablo adi."),
            new NodeParameterDefinition("columns", "Columns", NodeParameterType.Json, IsRequired: true,
                HelpText: "JSON dizisi: []"),
            new NodeParameterDefinition("syncMode", "Sync Mode", NodeParameterType.Select, DefaultValue: "createOnly",
                Options: ["createOnly", "addColumns"],
                HelpText: "createOnly: yalniz yoksa olustur. addColumns: tablo varsa eksik kolonlari ADD COLUMN ile ekler (veri korunur).")
        ],
        Tags: ["database", "schema", "ddl"],
        Icon: "table",
        Color: "#64748b",
        SubCategory: "Schema");

    public override async Task<NodeExecutionResult> ExecuteAsync(INodeExecutionContext context)
    {
        var state = DatabaseNodeHelpers.ReadState(context);
        if (state is null)
        {
            return NodeExecutionResult.Failure("Ensure Table bir database connection node'undan sonra calismalidir.");
        }

        var table = context.GetString("table");
        if (string.IsNullOrWhiteSpace(table))
        {
            return NodeExecutionResult.Failure("Tablo adi gereklidir.");
        }

        if (context.GetJson("columns") is not JsonArray columns || columns.Count == 0)
        {
            return NodeExecutionResult.Failure("'columns' en az bir kolon iceren bir JSON dizisi olmalidir.");
        }

        string sql;
        try
        {
            sql = BuildCreateTable(state.Provider, state.Schema, table, columns);
        }
        catch (InvalidOperationException ex)
        {
            return NodeExecutionResult.Failure(ex.Message);
        }

        await using var connection = await DatabaseNodeHelpers.OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return NodeExecutionResult.Failure("Database baglantisi kurulamadi.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(context.CancellationToken);

        // addColumns modu: tablo zaten varsa (veya yeni olusturulsa da) gridde olup tabloda
        // bulunmayan kolonlari ALTER TABLE ADD COLUMN ile ekler. Veri korunur. Eklenen kolonlara
        // PRIMARY KEY / NOT NULL uygulanmaz (SQLite ADD COLUMN bu kisitlari desteklemez).
        var added = new JsonArray();
        if (string.Equals(context.GetString("syncMode"), "addColumns", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await LoadExistingColumnsAsync(connection, state.Provider, state.Schema, table, context.CancellationToken);
            var target = DatabaseNodeHelpers.QuotePath(state.Provider, state.Schema, table);
            var addKeyword = state.Provider == DatabaseProvider.SqlServer ? "ADD" : "ADD COLUMN";

            foreach (var column in columns.OfType<JsonObject>())
            {
                var name = column["name"]?.ToString();
                var type = column["type"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || existing.Contains(name))
                {
                    continue;
                }

                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {target} {addKeyword} {DatabaseNodeHelpers.QuoteIdentifier(state.Provider, name)} {type};";
                await alter.ExecuteNonQueryAsync(context.CancellationToken);
                added.Add(name);
            }
        }

        // State'i ileri tasi ki downstream db.* node'lari ayni baglantiyi zincirleyebilsin.
        var output = DatabaseNodeHelpers.ToJson(state);
        output["table"] = table;
        output["ddl"] = sql;
        output["addedColumns"] = added;
        return NodeExecutionResult.Single(NodeItem.From(output));
    }

    /// <summary>Mevcut tablonun kolon adlarini saglayiciya gore okur (case-insensitive kume).</summary>
    private static async Task<HashSet<string>> LoadExistingColumnsAsync(
        System.Data.Common.DbConnection connection,
        DatabaseProvider provider,
        string? schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = provider switch
        {
            DatabaseProvider.Sqlite => "SELECT name FROM pragma_table_info(@table)",
            DatabaseProvider.SqlServer => "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND (@schema IS NULL OR TABLE_SCHEMA = @schema)",
            DatabaseProvider.MySql => "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table",
            _ => "SELECT column_name FROM information_schema.columns WHERE table_name = @table AND (@schema::text IS NULL OR table_schema = @schema::text)"
        };

        DatabaseNodeHelpers.AddParameter(command, "@table", table);
        if (provider != DatabaseProvider.MySql && provider != DatabaseProvider.Sqlite)
        {
            DatabaseNodeHelpers.AddParameter(command, "@schema", string.IsNullOrWhiteSpace(schema) ? null : schema);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string BuildCreateTable(DatabaseProvider provider, string? schema, string table, JsonArray columns)
    {
        var defs = new List<string>();
        foreach (var column in columns.OfType<JsonObject>())
        {
            var name = column["name"]?.ToString();
            var type = column["type"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
            {
                throw new InvalidOperationException("Her kolonun 'name' ve 'type' alani olmalidir.");
            }

            if (!TypeRegex().IsMatch(type))
            {
                throw new InvalidOperationException($"Gecersiz kolon tipi: {type}");
            }

            var builder = new StringBuilder();
            builder.Append(DatabaseNodeHelpers.QuoteIdentifier(provider, name)).Append(' ').Append(type);
            if (AsBool(column["pk"]))
            {
                builder.Append(" PRIMARY KEY");
            }

            if (AsBool(column["notnull"]))
            {
                builder.Append(" NOT NULL");
            }

            defs.Add(builder.ToString());
        }

        var target = DatabaseNodeHelpers.QuotePath(provider, schema, table);
        return $"CREATE TABLE IF NOT EXISTS {target} (\n  {string.Join(",\n  ", defs)}\n);";
    }

    private static bool AsBool(JsonNode? node) =>
        node is JsonValue value &&
        (value.TryGetValue<bool>(out var b) && b ||
         string.Equals(value.ToString(), "true", StringComparison.OrdinalIgnoreCase));

    // Tip ifadesi: harf/rakam/bosluk/parantez/virgul (orn. "TEXT", "VARCHAR(50)", "NUMERIC(10, 2)").
    [GeneratedRegex(@"^[A-Za-z0-9 (),]+$")]
    private static partial Regex TypeRegex();
}
