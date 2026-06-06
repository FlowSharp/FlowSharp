using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using FlowSharp.Application.Nodes;

namespace FlowSharp.Nodes.Database;

public enum DatabaseProvider
{
    Postgres,
    SqlServer,
    MySql,
    Sqlite
}

public sealed record DatabaseConnectionState(
    DatabaseProvider Provider,
    string CredentialType,
    string CredentialName,
    string? Database,
    string? Schema,
    // SQLite icin: cozulmus mutlak .db dosya yolu (credential yok, dosya tabanli).
    string? DataSource = null);

internal static class DatabaseNodeHelpers
{
    private const string StateKey = "_flowsharpDb";
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static JsonObject ToJson(DatabaseConnectionState state)
    {
        return new JsonObject
        {
            [StateKey] = true,
            ["provider"] = state.Provider.ToString(),
            ["credentialType"] = state.CredentialType,
            ["credentialName"] = state.CredentialName,
            ["database"] = state.Database,
            ["schema"] = state.Schema,
            ["dataSource"] = state.DataSource
        };
    }

    public static DatabaseConnectionState? ReadState(INodeExecutionContext context)
    {
        var json = context.Items.FirstOrDefault()?.Json;
        if (json is null ||
            !json.TryGetPropertyValue(StateKey, out var marker) ||
            marker is not JsonValue markerValue ||
            !markerValue.TryGetValue<bool>(out var isDatabaseState) ||
            !isDatabaseState)
        {
            return null;
        }

        return Enum.TryParse<DatabaseProvider>(json["provider"]?.ToString(), out var provider)
            ? new DatabaseConnectionState(
                provider,
                json["credentialType"]?.ToString() ?? "",
                json["credentialName"]?.ToString() ?? "",
                json["database"]?.ToString(),
                json["schema"]?.ToString(),
                json["dataSource"]?.ToString())
            : null;
    }

    public static string? ReadTable(INodeExecutionContext context) =>
        context.GetString("table") ??
        context.Items.FirstOrDefault()?.Json["table"]?.ToString();

    public static string? ReadSchema(INodeExecutionContext context, DatabaseConnectionState state) =>
        context.GetString("schema") ??
        context.Items.FirstOrDefault()?.Json["schema"]?.ToString() ??
        state.Schema;

    public static async Task<DbConnection?> OpenConnectionAsync(
        DatabaseConnectionState state,
        INodeExecutionContext context)
    {
        // SQLite credential degil dosya tabanlidir: baglanti dizesi DataSource'tan kurulur.
        if (state.Provider == DatabaseProvider.Sqlite)
        {
            if (string.IsNullOrWhiteSpace(state.DataSource))
            {
                return null;
            }

            var sqlite = new SqliteConnection($"Data Source={state.DataSource};Pooling=True");
            await sqlite.OpenAsync(context.CancellationToken);
            await ApplySqlitePragmasAsync(sqlite, context.CancellationToken);
            return sqlite;
        }

        var connectionString = await ResolveConnectionStringAsync(state.Provider, state.CredentialType, state.CredentialName, context, state.Database);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var connection = CreateConnection(state.Provider, connectionString);
        await connection.OpenAsync(context.CancellationToken);
        return connection;
    }

    /// <summary>
    /// SQLite baglantisi acilir acilmaz: foreign key kisitlarini etkinlestir (varsayilan KAPALI),
    /// eszamanli yazma icin WAL modu ve kilit beklemesi icin busy_timeout uygula.
    /// </summary>
    public static async Task ApplySqlitePragmasAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<string?> ResolveConnectionStringAsync(
        DatabaseProvider provider,
        string credentialType,
        string credentialName,
        INodeExecutionContext context,
        string? databaseOverride = null)
    {
        var conn = await context.GetCredentialAsync(credentialType, credentialName, "connectionString");
        if (!string.IsNullOrWhiteSpace(conn))
        {
            return conn;
        }

        var host = await context.GetCredentialAsync(credentialType, credentialName, "host");
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var port = int.TryParse(await context.GetCredentialAsync(credentialType, credentialName, "port"), out var parsedPort)
            ? parsedPort
            : DefaultPort(provider);
        // Database, Connection node'unun DATABASE alanindan gelir; credential'da tutulmaz.
        var database = !string.IsNullOrWhiteSpace(databaseOverride)
            ? databaseOverride
            : await context.GetCredentialAsync(credentialType, credentialName, "database");
        var user = await context.GetCredentialAsync(credentialType, credentialName, "user");
        var password = await context.GetCredentialAsync(credentialType, credentialName, "password");
        var ssl = string.Equals(await context.GetCredentialAsync(credentialType, credentialName, "ssl"), "true", StringComparison.OrdinalIgnoreCase);
        var integratedSecurity = string.Equals(await context.GetCredentialAsync(credentialType, credentialName, "integratedSecurity"), "true", StringComparison.OrdinalIgnoreCase);

        return CreateConnectionStringBuilder(provider, host, port, database, user, password, ssl, integratedSecurity).ConnectionString;
    }

    public static DbConnection CreateConnection(DatabaseProvider provider, string connectionString) =>
        provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnection(connectionString),
            DatabaseProvider.MySql => new MySqlConnection(connectionString),
            DatabaseProvider.Sqlite => new SqliteConnection(connectionString),
            _ => new NpgsqlConnection(connectionString)
        };

    public static DbConnectionStringBuilder CreateConnectionStringBuilder(
        DatabaseProvider provider,
        string host,
        int port,
        string? database,
        string? user,
        string? password,
        bool ssl,
        bool integratedSecurity = false) =>
        provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnectionStringBuilder
            {
                // Named instance / LocalDB (orn. "(localdb)\MSSQLLocalDB" veya "host\SQLEXPRESS")
                // port kabul etmez; sadece duz host'a ",port" eklenir.
                DataSource = host.Contains('\\', StringComparison.Ordinal) || host.StartsWith('(')
                    ? host
                    : $"{host},{port}",
                InitialCatalog = database,
                // Integrated Security (Windows kimligi) acikken kullanici/parola gonderilmez.
                IntegratedSecurity = integratedSecurity,
                UserID = integratedSecurity ? "" : user,
                Password = integratedSecurity ? "" : password,
                Encrypt = ssl,
                TrustServerCertificate = true
            },
            DatabaseProvider.MySql => new MySqlConnectionStringBuilder
            {
                Server = host,
                Port = (uint)port,
                Database = database,
                UserID = user,
                Password = password,
                SslMode = ssl ? MySqlSslMode.Required : MySqlSslMode.Preferred
            },
            _ => new NpgsqlConnectionStringBuilder
            {
                Host = host,
                Port = port,
                Database = database,
                Username = user,
                Password = password,
                SslMode = ssl ? SslMode.Require : SslMode.Prefer
            }
        };

    public static int DefaultPort(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.SqlServer => 1433,
            DatabaseProvider.MySql => 3306,
            _ => 5432
        };

    public static string QuotePath(DatabaseProvider provider, string? schema, string table)
    {
        var parts = new List<string>();
        if (!table.Contains('.', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(schema))
        {
            parts.Add(schema);
        }

        parts.AddRange(table.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.Join(".", parts.Select(part => QuoteIdentifier(provider, part)));
    }

    public static string QuoteIdentifier(DatabaseProvider provider, string identifier)
    {
        if (!IdentifierRegex.IsMatch(identifier))
        {
            throw new InvalidOperationException($"Gecersiz SQL identifier: {identifier}");
        }

        return provider switch
        {
            DatabaseProvider.SqlServer => $"[{identifier}]",
            DatabaseProvider.MySql => $"`{identifier}`",
            _ => $"\"{identifier}\""
        };
    }

    public static Dictionary<string, JsonNode?> ReadColumns(INodeExecutionContext context, string parameter = "columnsJson")
    {
        var json = context.GetJson(parameter);
        // Her kolon degeri bir ifade ({{...}}) icerebilir; ResolveValue ile tek tek cozumlenir.
        // (GetJson yalniz tum parametre tek ifadeyse cozer; obje icindeki alanlari cozmez.)
        return json as JsonObject is { } obj
            ? obj.ToDictionary(pair => pair.Key, pair => context.ResolveValue(pair.Value))
            : [];
    }

    public static void AddParameter(DbCommand command, string name, JsonNode? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = ToDbValue(value);
        command.Parameters.Add(parameter);
    }

    public static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    public static object ToDbValue(JsonNode? node)
    {
        if (node is null)
        {
            return DBNull.Value;
        }

        var value = JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
        return value.ValueKind switch
        {
            JsonValueKind.Null => DBNull.Value,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt64(out var l) => l,
            JsonValueKind.Number when value.TryGetDecimal(out var d) => d,
            JsonValueKind.String => value.GetString() ?? "",
            _ => value.GetRawText()
        };
    }

    public static JsonNode? ToJson(object? value) => value switch
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
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value.ToString()
    };

    public static async Task<NodeExecutionResult> ReadRowsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var items = new List<NodeItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
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

    public static NodeExecutionResult AffectedRows(int affected) =>
        NodeExecutionResult.Single(NodeItem.From(new JsonObject { ["affectedRows"] = affected }));

    public static string BuildWhere(string? where) =>
        string.IsNullOrWhiteSpace(where) ? "" : $" WHERE {where}";

    /// <summary>
    /// Upstream baglantidaki tablolari dropdown secenekleri olarak listeler.
    /// Tum database node'larinin "table" parametresi bunu kullanir (IHasDynamicOptions).
    /// </summary>
    public static async Task<IReadOnlyList<NodeParameterOption>> ListTableOptionsAsync(INodeExecutionContext context)
    {
        var state = ReadState(context);
        if (state is null)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return [];
        }

        var schema = ReadSchema(context, state);
        await using var command = connection.CreateCommand();
        command.CommandText = state.Provider switch
        {
            DatabaseProvider.SqlServer => string.IsNullOrWhiteSpace(schema)
                ? "SELECT TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME"
                : "SELECT TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = @schema ORDER BY TABLE_NAME",
            DatabaseProvider.MySql => "SELECT TABLE_NAME AS table_name FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME",
            DatabaseProvider.Sqlite => "SELECT name AS table_name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name",
            _ => string.IsNullOrWhiteSpace(schema)
                ? "SELECT table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog', 'information_schema') ORDER BY table_name"
                : "SELECT table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE' AND table_schema = @schema ORDER BY table_name"
        };

        if (!string.IsNullOrWhiteSpace(schema) && state.Provider != DatabaseProvider.MySql)
        {
            AddParameter(command, "@schema", schema);
        }

        var options = new List<NodeParameterOption>();
        await using var reader = await command.ExecuteReaderAsync(context.CancellationToken);
        while (await reader.ReadAsync(context.CancellationToken))
        {
            var name = reader.GetString(0);
            options.Add(new NodeParameterOption(name, name));
        }

        return options;
    }

    /// <summary>
    /// Secili tablonun kolonlarini dropdown/secenek olarak listeler. db.insert/update/upsert
    /// gibi node'larin "columnsJson" alaninda kolon-bazli deger formu kurmak icin kullanilir.
    /// </summary>
    public static async Task<IReadOnlyList<NodeParameterOption>> ListColumnOptionsAsync(INodeExecutionContext context)
    {
        var state = ReadState(context);
        if (state is null)
        {
            return [];
        }

        var table = ReadTable(context);
        if (string.IsNullOrWhiteSpace(table))
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(state, context);
        if (connection is null)
        {
            return [];
        }

        var schema = ReadSchema(context, state);
        await using var command = connection.CreateCommand();
        // Her satir: (kolon adi, pk bayragi). PK bayragi UI'da gosterilir ("pk") veya bos.
        // SQLite: pragma_table_info tablo-degerli fonksiyona parametre binding guvenilir degil;
        // tablo adi dogrulanmis bir string literal olarak gomulur (tek tirnak escape edilir).
        command.CommandText = state.Provider switch
        {
            DatabaseProvider.Sqlite =>
                $"SELECT name, CASE WHEN pk > 0 THEN 'pk' ELSE '' END FROM pragma_table_info('{table.Replace("'", "''")}') ORDER BY cid",
            DatabaseProvider.SqlServer =>
                "SELECT COLUMN_NAME, '' FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @table AND (@schema IS NULL OR TABLE_SCHEMA = @schema) ORDER BY ORDINAL_POSITION",
            DatabaseProvider.MySql =>
                "SELECT COLUMN_NAME, CASE WHEN COLUMN_KEY = 'PRI' THEN 'pk' ELSE '' END FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table ORDER BY ORDINAL_POSITION",
            _ =>
                "SELECT column_name, '' FROM information_schema.columns WHERE table_name = @table AND (@schema::text IS NULL OR table_schema = @schema::text) ORDER BY ordinal_position"
        };

        if (state.Provider != DatabaseProvider.Sqlite)
        {
            AddParameter(command, "@table", table);
        }

        if (state.Provider != DatabaseProvider.MySql && state.Provider != DatabaseProvider.Sqlite)
        {
            AddParameter(command, "@schema", string.IsNullOrWhiteSpace(schema) ? null : schema);
        }

        var options = new List<NodeParameterOption>();
        await using var reader = await command.ExecuteReaderAsync(context.CancellationToken);
        while (await reader.ReadAsync(context.CancellationToken))
        {
            var name = reader.GetString(0);
            var pk = !reader.IsDBNull(1) && reader.GetString(1) == "pk";
            options.Add(new NodeParameterOption(name, pk ? "pk" : ""));
        }

        return options;
    }
}
