namespace FlowSharp.Application.Errors;

/// <summary>
/// Bir exception turunu/durumunu kullaniciya gosterilecek mesaja ceviren tek kural.
/// Katmanlar kendi kurallarini (orn. AI servis hatalari, veritabani hatalari) <see cref="IErrorTranslator"/>'a
/// ekleyerek ceviriciyi genisletir; mevcut kod degismeden yeni hata tipleri tanitilabilir.
/// </summary>
public sealed record ErrorRule(Func<Exception, bool> Match, Func<Exception, UserFacingError> Describe)
{
    /// <summary>Belirli bir exception turu icin, exception ornegine bagli mesaj ureten kural.</summary>
    public static ErrorRule For<TException>(Func<TException, UserFacingError> describe)
        where TException : Exception =>
        new(exception => exception is TException, exception => describe((TException)exception));

    /// <summary>Belirli bir exception turu icin sabit mesajli kural.</summary>
    public static ErrorRule For<TException>(string message, ErrorCategory category)
        where TException : Exception =>
        For<TException>(_ => new UserFacingError(message, category));
}

/// <summary>Exception'lari kullaniciya gosterilecek mesaja ceviren merkezi servis.</summary>
public interface IErrorTranslator
{
    UserFacingError Translate(Exception exception);

    /// <summary>Calisma zamaninda (genelde baslangicta) yeni bir cevirme kurali ekler.</summary>
    void AddRule(ErrorRule rule);
}
