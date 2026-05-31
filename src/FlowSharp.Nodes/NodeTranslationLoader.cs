using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowSharp.Application.Localization;

namespace FlowSharp.Nodes;

/// <summary>
/// Built-in node cevirilerini, Nodes assembly'sine gomulu <c>**/lang/&lt;culture&gt;.json</c>
/// kaynaklarindan okuyup <see cref="INodeTranslationStore"/>'a yukler. Boylece her node grubu
/// kendi cevirisini yaninda tasir (merkezi bir dosya yok).
/// </summary>
public sealed class NodeTranslationLoader(
    INodeTranslationStore store,
    ILogger<NodeTranslationLoader> logger) : IHostedService
{
    private const string Marker = ".lang.";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(NodeTranslationLoader).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var idx = resource.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0 || !resource.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // ...lang.<culture>.json -> culture
            var culture = resource[(idx + Marker.Length)..^5];
            try
            {
                using var stream = assembly.GetManifestResourceStream(resource);
                if (stream is null)
                {
                    continue;
                }

                using var sr = new StreamReader(stream);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(sr.ReadToEnd());
                if (map is not null)
                {
                    // Her kaynak benzersiz source ile eklenir; built-in oldugu icin kaldirilmaz.
                    store.Set($"builtin:{resource}", culture, map);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Node ceviri kaynagi okunamadi: {Resource}", resource);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
