using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Abstractions;
using FlowSharp.Application.Workflows;

namespace FlowSharp.Infrastructure.Storage;

/// <summary>
/// <see cref="IBlobStore"/>'un dosya sistemi implementasyonu. Her icerik, yapilandirilan kok dizin
/// altinda rastgele bir anahtarla <c>.json</c> dosyasi olarak saklanir. Anahtar uzayi cok buyudugunde
/// dizin performansi icin ilk iki karaktere gore alt klasore ayrilir (sharding).
/// </summary>
public sealed class FileSystemBlobStore : IBlobStore
{
    private readonly string rootDirectory;
    private readonly ILogger<FileSystemBlobStore> logger;

    public FileSystemBlobStore(IOptions<BlobStorageOptions> options, ILogger<FileSystemBlobStore> logger)
    {
        rootDirectory = Path.GetFullPath(options.Value.Directory);
        this.logger = logger;
        Directory.CreateDirectory(rootDirectory);
    }

    public async Task<string> SaveAsync(string content, CancellationToken cancellationToken = default)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
        return key;
    }

    public async Task<string?> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var path = PathFor(reference);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
    }

    public Task DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = PathFor(reference);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            // Silme hatasi (orn. erisim) calismayi durdurmamali; yetim blob en fazla disk yer kaplar.
            logger.LogWarning(exception, "Blob silinemedi: {Reference}", reference);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        // Yol enjeksiyonu/gecersiz anahtar korumasi: yalniz dosya adi parcasini kullan.
        var safeKey = Path.GetFileName(key);
        var shard = safeKey.Length >= 2 ? safeKey[..2] : "_";
        return Path.Combine(rootDirectory, shard, $"{safeKey}.json");
    }
}
