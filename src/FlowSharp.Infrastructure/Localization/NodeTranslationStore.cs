using System.Collections.Concurrent;
using FlowSharp.Application.Localization;

namespace FlowSharp.Infrastructure.Localization;

/// <summary>
/// Bellek-ici node ceviri deposu. Kaynak (built-in/plugin) basina cevirileri tutar; okuma icin
/// dil -> anahtar -> deger birlesik gorunumu uretir. Thread-safe.
/// </summary>
public sealed class NodeTranslationStore : INodeTranslationStore
{
    public const string DefaultCulture = "tr";

    private readonly object gate = new();
    // source -> culture -> (key -> value)
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> bySource =
        new(StringComparer.OrdinalIgnoreCase);
    // culture -> (key -> value)  (birlesik gorunum)
    private ConcurrentDictionary<string, Dictionary<string, string>> merged = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string source, string culture, IReadOnlyDictionary<string, string> entries)
    {
        lock (gate)
        {
            if (!bySource.TryGetValue(source, out var cultures))
            {
                cultures = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                bySource[source] = cultures;
            }

            cultures[culture] = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase);
            Rebuild();
        }
    }

    public void Remove(string source)
    {
        lock (gate)
        {
            if (bySource.Remove(source))
            {
                Rebuild();
            }
        }
    }

    public string? Get(string culture, string key)
    {
        if (merged.TryGetValue(culture, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (merged.TryGetValue(DefaultCulture, out var fallback) && fallback.TryGetValue(key, out var fb))
        {
            return fb;
        }

        return null;
    }

    private void Rebuild()
    {
        var next = new ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cultures in bySource.Values)
        {
            foreach (var (culture, entries) in cultures)
            {
                var map = next.GetOrAdd(culture, _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                foreach (var (key, value) in entries)
                {
                    map[key] = value;
                }
            }
        }

        merged = next;
    }
}
