namespace FlowSharp.Application.Errors;

/// <summary>
/// Tum catch noktalarinin kullandigi kisa yol: <c>exception.ToUserMessage()</c>.
/// Merkezi <see cref="ErrorTranslator.Default"/> uzerinden cevirir; davranis tek yerden yonetilir.
/// </summary>
public static class ExceptionExtensions
{
    public static string ToUserMessage(this Exception exception) =>
        ErrorTranslator.Default.Translate(exception).Message;

    public static UserFacingError ToUserFacingError(this Exception exception) =>
        ErrorTranslator.Default.Translate(exception);
}
