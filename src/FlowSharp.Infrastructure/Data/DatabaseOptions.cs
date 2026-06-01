namespace FlowSharp.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = DatabaseProviders.Postgres;

    public bool ApplyMigrationsOnStartup { get; set; } = true;
}

public static class DatabaseProviders
{
    public const string Postgres = "Postgres";
    public const string SqlServer = "SqlServer";
    public const string Sqlite = "Sqlite";

    public static string Normalize(string? provider) =>
        provider?.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "npgsql" => Postgres,
            "sqlserver" or "mssql" or "sql-server" => SqlServer,
            "sqlite" or "sqlite3" => Sqlite,
            _ => Postgres
        };
}
