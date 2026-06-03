namespace FlowSharp.Application.Errors;

/// <summary>Hatanin kullaniciya gosterilebilecek genel kategorisi (UI ikon/renk vb. icin de kullanilabilir).</summary>
public enum ErrorCategory
{
    Unknown,
    Network,
    Timeout,
    Configuration,
    Authentication,
    RateLimit,
    ExternalService,
    Cancelled,
    Data
}

/// <summary>
/// Bir exception'in son kullaniciya gosterilecek hali: kisa, eyleme donuk ve guvenli (stack/secret icermez)
/// bir mesaj + kabaca kategorisi. Tam teknik detay her zaman loglara yazilir.
/// </summary>
public sealed record UserFacingError(string Message, ErrorCategory Category = ErrorCategory.Unknown);
