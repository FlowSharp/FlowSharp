using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FlowSharp.Application.Localization;
using FlowSharp.Application.Nodes;
using FlowSharp.Application.Plugins;

namespace FlowSharp.Infrastructure.Plugins;

/// <summary>
/// "plugins/" altindaki her klasoru bagimsiz bir plugin olarak ele alir; icindeki tum .cs
/// kaynak dosyalarini (alt klasorler dahil) Roslyn ile derleyip collectible bir
/// <see cref="AssemblyLoadContext"/>'e yukler ve bulunan <see cref="INodeType"/>'lari
/// node kaydina ekler. Boylece ana uygulama yeniden derlenmeden node eklenebilir.
/// </summary>
public sealed class PluginManager(
    INodeRegistry registry,
    INodeTranslationStore translations,
    IHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    IOptions<PluginOptions> options,
    ILogger<PluginManager> logger) : IPluginManager
{
    private readonly object gate = new();
    private readonly Dictionary<string, LoadedPlugin> plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly PluginOptions settings = options.Value;

    public string? OfficialMarketplaceUrl => settings.OfficialMarketplaceUrl;

    private string PluginsRoot => Path.IsPathRooted(settings.Path)
        ? settings.Path
        : Path.Combine(environment.ContentRootPath, settings.Path);

    public IReadOnlyList<PluginInfo> List()
    {
        lock (gate)
        {
            return plugins.Values.Select(p => p.Info).ToArray();
        }
    }

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(PluginsRoot);
        foreach (var dir in Directory.GetDirectories(PluginsRoot))
        {
            var name = Path.GetFileName(dir);
            try
            {
                await Task.Run(() => LoadPlugin(name), cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Plugin '{Name}' yuklenemedi.", name);
            }
        }
    }

    public Task<PluginInfo> ReloadAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadPlugin(name), cancellationToken);

    public Task RemoveAsync(string name, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            Unload(name);
            var dir = Path.Combine(PluginsRoot, SafeName(name));
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }, cancellationToken);

    public async Task<PluginInfo> InstallFromGitHubAsync(string repoUrl, CancellationToken cancellationToken = default)
    {
        var (owner, repo, branch, subPath) = ParseGitHubUrl(repoUrl);
        Directory.CreateDirectory(PluginsRoot);

        var client = httpClientFactory.CreateClient("workflow-nodes");
        var branches = branch is not null ? [branch] : new[] { "main", "master" };

        byte[]? zipBytes = null;
        string usedBranch = branches[0];
        foreach (var b in branches)
        {
            var url = $"https://codeload.github.com/{owner}/{repo}/zip/refs/heads/{b}";
            using var resp = await client.GetAsync(url, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                zipBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
                usedBranch = b;
                break;
            }
        }

        if (zipBytes is null)
        {
            throw new InvalidOperationException($"'{owner}/{repo}' deposu indirilemedi (branch: {string.Join("/", branches)}).");
        }

        var pluginName = SafeName(string.IsNullOrEmpty(subPath) ? repo : Path.GetFileName(subPath.TrimEnd('/')));
        var targetDir = Path.Combine(PluginsRoot, pluginName);
        if (Directory.Exists(targetDir))
        {
            Unload(pluginName);
            Directory.Delete(targetDir, recursive: true);
        }

        // Zip icinde tek bir kok klasor olur: "{repo}-{branch}/...". Istenen alt yolu cikar.
        using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
        {
            var rootPrefix = $"{repo}-{usedBranch}/";
            var innerPrefix = rootPrefix + (string.IsNullOrEmpty(subPath) ? "" : subPath.Trim('/') + "/");

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || !entry.FullName.StartsWith(innerPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var relative = entry.FullName[innerPrefix.Length..];
                var destPath = Path.Combine(targetDir, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                entry.ExtractToFile(destPath, overwrite: true);
            }
        }

        if (!Directory.Exists(targetDir))
        {
            throw new InvalidOperationException("Indirilen depoda kopyalanacak dosya bulunamadi (alt yol hatali olabilir).");
        }

        return await Task.Run(() => LoadPlugin(pluginName), cancellationToken);
    }

    private PluginInfo LoadPlugin(string name)
    {
        var safe = SafeName(name);
        var dir = Path.Combine(PluginsRoot, safe);

        // Onceki yuklemeyi (varsa) temizle.
        Unload(safe);

        PluginInfo info;
        if (!Directory.Exists(dir))
        {
            info = new PluginInfo(safe, false, "Plugin klasoru bulunamadi.", [], null);
        }
        else
        {
            var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
            if (sources.Length == 0)
            {
                info = new PluginInfo(safe, false, "Plugin klasorunde .cs dosyasi yok.", [], null);
            }
            else
            {
                info = Compile(safe, sources);
            }

            // Plugin kendi cevirilerini yaninda getirir: plugins/<plugin>/lang/<culture>.json
            if (info.Loaded)
            {
                LoadPluginTranslations(safe, dir);
            }
        }

        lock (gate)
        {
            // Hata durumunda da listede gozuksun.
            if (!plugins.ContainsKey(safe))
            {
                plugins[safe] = new LoadedPlugin { Info = info };
            }
            else
            {
                plugins[safe].Info = info;
            }
        }

        return info;
    }

    // plugins/<plugin>/lang/<culture>.json dosyalarini node ceviri deposuna ekler.
    private void LoadPluginTranslations(string pluginName, string dir)
    {
        var langDir = Path.Combine(dir, "lang");
        if (!Directory.Exists(langDir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(langDir, "*.json"))
        {
            try
            {
                var culture = Path.GetFileNameWithoutExtension(file);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                if (map is not null)
                {
                    translations.Set($"plugin:{pluginName}", culture, map);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Plugin '{Name}' dil dosyasi okunamadi: {File}", pluginName, file);
            }
        }
    }

    private PluginInfo Compile(string name, string[] sources)
    {
        var syntaxTrees = sources.Select(path =>
            CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToArray();

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            assemblyName: $"Plugin_{name}_{Guid.NewGuid():N}",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = string.Join("; ", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(10)
                .Select(d => d.ToString()));
            logger.LogWarning("Plugin '{Name}' derleme hatasi: {Errors}", name, errors);
            return new PluginInfo(name, false, $"Derleme hatasi: {errors}", [], null);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var context = new PluginLoadContext(name);
        var assembly = context.LoadFromStream(ms);

        var nodeTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(INodeType).IsAssignableFrom(t))
            .ToArray();

        if (nodeTypes.Length == 0)
        {
            context.Unload();
            return new PluginInfo(name, false, "INodeType implementasyonu bulunamadi.", [], null);
        }

        var nodeInfos = new List<PluginNodeInfo>();
        var keys = new List<string>();
        foreach (var type in nodeTypes)
        {
            if (Activator.CreateInstance(type) is not INodeType node)
            {
                continue;
            }

            registry.Register(node);
            keys.Add(node.Definition.Key);
            nodeInfos.Add(new PluginNodeInfo(node.Definition.Key, node.Definition.DisplayName, node.Definition.Category.ToString()));
        }

        var info = new PluginInfo(name, true, null, nodeInfos, DateTimeOffset.UtcNow);
        lock (gate)
        {
            plugins[name] = new LoadedPlugin { Context = context, NodeKeys = keys, Info = info };
        }

        logger.LogInformation("Plugin '{Name}' yuklendi: {Count} node.", name, keys.Count);
        return info;
    }

    private void Unload(string name)
    {
        LoadedPlugin? existing;
        lock (gate)
        {
            plugins.TryGetValue(name, out existing);
        }

        if (existing is null)
        {
            return;
        }

        foreach (var key in existing.NodeKeys)
        {
            registry.Unregister(key);
        }

        translations.Remove($"plugin:{name}");
        existing.Context?.Unload();

        lock (gate)
        {
            plugins.Remove(name);
        }
    }

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.').ToArray());
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "." or "..")
        {
            throw new InvalidOperationException($"Gecersiz plugin adi: '{name}'.");
        }

        return cleaned;
    }

    private static (string Owner, string Repo, string? Branch, string? SubPath) ParseGitHubUrl(string url)
    {
        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Yalniz https://github.com/owner/repo bicimindeki URL'ler desteklenir.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("URL 'owner/repo' icermeli.");
        }

        var owner = segments[0];
        var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];

        // .../tree/{branch}/{subPath...}
        string? branch = null;
        string? subPath = null;
        if (segments.Length >= 4 && segments[2].Equals("tree", StringComparison.OrdinalIgnoreCase))
        {
            branch = segments[3];
            if (segments.Length > 4)
            {
                subPath = string.Join('/', segments[4..]);
            }
        }

        return (owner, repo, branch, subPath);
    }

    private sealed class LoadedPlugin
    {
        public PluginLoadContext? Context { get; init; }
        public IReadOnlyList<string> NodeKeys { get; init; } = [];
        public PluginInfo Info { get; set; } = null!;
    }

    /// <summary>Collectible context: paylasilan tipler (INodeType vb.) default context'ten cozulur.</summary>
    private sealed class PluginLoadContext(string name) : AssemblyLoadContext($"plugin-{name}", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => null;
    }
}
