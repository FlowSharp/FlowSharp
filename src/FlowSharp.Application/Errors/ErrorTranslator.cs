using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace FlowSharp.Application.Errors;

/// <summary>
/// Tum try/catch noktalarinin paylastigi merkezi exception -> kullanici mesaji cevirici.
/// Onceden tanimli (framework geneli) kurallarla gelir; katmanlar <see cref="AddRule"/> ile
/// kendi kurallarini ekleyerek genisletir. Eslesme bulunamazsa hatanin kisa/guvenli mesaji dondurulur.
/// Sonradan eklenen kurallar daha spesifik kabul edilip once denenir.
/// </summary>
public sealed class ErrorTranslator : IErrorTranslator
{
    private readonly Lock gate = new();
    private ErrorRule[] rules;

    public ErrorTranslator(IEnumerable<ErrorRule> initialRules) => rules = initialRules.ToArray();

    /// <summary>Surec genelinde paylasilan, onceden tanimli kurallarla gelen ortak ornek.</summary>
    public static ErrorTranslator Default { get; } = new(BuiltInRules());

    public void AddRule(ErrorRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (gate)
        {
            rules = [rule, .. rules];
        }
    }

    public UserFacingError Translate(Exception exception)
    {
        var inner = Unwrap(exception);
        var snapshot = rules;
        foreach (var rule in snapshot)
        {
            if (rule.Match(inner))
            {
                return rule.Describe(inner);
            }
        }

        return new UserFacingError(ShortMessage(inner), ErrorCategory.Unknown);
    }

    /// <summary>AggregateException/TargetInvocationException gibi sarmal hatalarin asil ic hatasini bulur.</summary>
    public static Exception Unwrap(Exception exception) =>
        exception is AggregateException { InnerException: { } inner } ? Unwrap(inner) : exception;

    /// <summary>Mesajin ilk anlamli satirini, 200 karaktere kisaltarak ve guvenli sekilde dondurur.</summary>
    public static string ShortMessage(Exception exception)
    {
        var firstLine = (exception.Message ?? string.Empty)
            .Split('\n', '\r')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))?
            .Trim() ?? string.Empty;

        return firstLine.Length > 200 ? string.Concat(firstLine.AsSpan(0, 200), "…") : firstLine;
    }

    private static IEnumerable<ErrorRule> BuiltInRules() =>
    [
        // Zaman asimi: HTTP istemcileri timeout'u TaskCanceledException olarak firlatir.
        ErrorRule.For<TimeoutException>("Islem zaman asimina ugradi. Biraz sonra tekrar deneyin.", ErrorCategory.Timeout),
        ErrorRule.For<TaskCanceledException>("Islem zaman asimina ugradi. Biraz sonra tekrar deneyin.", ErrorCategory.Timeout),

        // Ag / baglanti
        ErrorRule.For<HttpRequestException>("Uzak servise ulasilamadi. Adresi ve ag/internet baglantisini kontrol edin.", ErrorCategory.Network),
        ErrorRule.For<SocketException>("Ag baglantisi kurulamadi. Hedef adresi ve baglantiyi kontrol edin.", ErrorCategory.Network),

        // Yapilandirma / veri
        ErrorRule.For<UriFormatException>("Adres (URL) gecersiz. Girdiginiz endpoint/URL'yi kontrol edin.", ErrorCategory.Configuration),
        ErrorRule.For<JsonException>("Veri ayristirilamadi (gecersiz JSON). Girdi formatini kontrol edin.", ErrorCategory.Data),

        // Gercek iptal (catch noktalari bunu genelde yeniden firlatir; yine de bir mesajimiz olsun).
        ErrorRule.For<OperationCanceledException>("Islem iptal edildi.", ErrorCategory.Cancelled),

        // Bizim attigimiz alan/durum hatalari genelde zaten kullanici dostu mesaj tasir: oldugu gibi goster.
        new ErrorRule(
            exception => exception is ArgumentException or NotSupportedException or InvalidOperationException,
            exception => new UserFacingError(ShortMessage(exception), ErrorCategory.Configuration))
    ];
}
