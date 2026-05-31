using System.Globalization;
using System.Text.Json;

namespace FlowSharp.Web.Localization;

/// <summary>
/// lang/*.json dosyalarini acilista yukleyen basit yerellestirici. Mevcut dil
/// <see cref="CultureInfo.CurrentUICulture"/>'dan okunur; bulunamazsa varsayilan dile,
/// o da yoksa anahtarin kendisine duser.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    public const string DefaultCulture = "tr";

    private readonly Dictionary<string, Dictionary<string, string>> byCulture =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CultureOption> supported = [];

    public JsonLocalizer(IWebHostEnvironment environment)
    {
        var dir = Path.Combine(environment.ContentRootPath, "lang");
        if (!Directory.Exists(dir))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(file);
            try
            {
                var json = File.ReadAllText(file);
                var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
                byCulture[code] = map;

                var display = TryNativeName(code);
                supported.Add(new CultureOption(code, display));
            }
            catch
            {
                // Bozuk dil dosyasi digerlerini etkilemesin.
            }
        }
    }

    public string this[string key] => Resolve(key) ?? key;

    public IReadOnlyList<CultureOption> SupportedCultures => supported;

    private string? Resolve(string key)
    {
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        if (byCulture.TryGetValue(culture, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (byCulture.TryGetValue(DefaultCulture, out var fallbackMap) && fallbackMap.TryGetValue(key, out var fallback))
        {
            return fallback;
        }

        return null;
    }

    private static string TryNativeName(string code)
    {
        try
        {
            var name = CultureInfo.GetCultureInfo(code).NativeName;
            return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : code.ToUpperInvariant();
        }
        catch
        {
            return code.ToUpperInvariant();
        }
    }
}
