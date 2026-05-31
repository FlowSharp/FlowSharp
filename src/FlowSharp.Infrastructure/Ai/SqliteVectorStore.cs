using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Ai;

namespace FlowSharp.Infrastructure.Ai;

/// <summary>
/// Basit SQLite tabanli vektor deposu. Her <c>scope</c> (workspace/workflow) icin ayri bir
/// .db dosyasi tutulur; boylece RAG verisi workspace'ler arasinda izole kalir (global degil).
/// Aramada koleksiyonun tum vektorleri okunup kosinus benzerligiyle en yakin K kayit dondurulur.
/// </summary>
public sealed class SqliteVectorStore : IVectorStore
{
    private readonly string directory;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly HashSet<string> initializedFiles = new(StringComparer.OrdinalIgnoreCase);

    public SqliteVectorStore(IHostEnvironment environment, IOptions<RagOptions> options)
    {
        var dir = options.Value.DatabaseDirectory;
        directory = Path.IsPathRooted(dir) ? dir : Path.Combine(environment.ContentRootPath, dir);
        Directory.CreateDirectory(directory);
    }

    private string ConnectionStringFor(string scope)
    {
        var safe = SafeScope(scope);
        return $"Data Source={Path.Combine(directory, safe + ".db")}";
    }

    private async Task<SqliteConnection> OpenAsync(string scope, CancellationToken cancellationToken)
    {
        var connectionString = ConnectionStringFor(scope);
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        if (!initializedFiles.Contains(connectionString))
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS vectors (
                    collection TEXT NOT NULL,
                    id         TEXT NOT NULL,
                    text       TEXT NOT NULL,
                    metadata   TEXT,
                    vector     BLOB NOT NULL,
                    PRIMARY KEY (collection, id)
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            initializedFiles.Add(connectionString);
        }

        return connection;
    }

    public async Task UpsertAsync(string scope, string collection, IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(scope, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var record in records)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO vectors (collection, id, text, metadata, vector)
                    VALUES ($c, $i, $t, $m, $v)
                    ON CONFLICT(collection, id) DO UPDATE SET text = $t, metadata = $m, vector = $v;
                    """;
                cmd.Parameters.AddWithValue("$c", collection);
                cmd.Parameters.AddWithValue("$i", record.Id);
                cmd.Parameters.AddWithValue("$t", record.Text);
                cmd.Parameters.AddWithValue("$m", (object?)record.Metadata ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$v", ToBytes(record.Vector));
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string scope, string collection, float[] query, int topK, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(scope, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, text, metadata, vector FROM vectors WHERE collection = $c;";
        cmd.Parameters.AddWithValue("$c", collection);

        var scored = new List<VectorMatch>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vector = ToFloats((byte[])reader["vector"]);
            scored.Add(new VectorMatch(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                CosineSimilarity(query, vector)));
        }

        return scored.OrderByDescending(m => m.Score).Take(Math.Max(1, topK)).ToList();
    }

    public async Task ClearAsync(string scope, string collection, CancellationToken cancellationToken = default)
    {
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(scope, cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM vectors WHERE collection = $c;";
            cmd.Parameters.AddWithValue("$c", collection);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static string SafeScope(string scope)
    {
        var cleaned = new string((scope ?? "global").Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "global" : cleaned;
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ToFloats(byte[] bytes)
    {
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return 0f;
        }

        double dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var denom = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denom == 0 ? 0f : (float)(dot / denom);
    }
}
