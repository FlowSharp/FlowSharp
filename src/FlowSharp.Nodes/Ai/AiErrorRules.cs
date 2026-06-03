using System.Net;
using Microsoft.SemanticKernel;
using FlowSharp.Application.Errors;

namespace FlowSharp.Nodes.Ai;

/// <summary>
/// AI/model servislerine ozgu hata cevirme kurallari. Merkezi <see cref="IErrorTranslator"/>'a
/// kaydedilir; boylece AI hatalari da diger tum hatalarla ayni yerden, tutarli bicimde yonetilir.
/// </summary>
internal static class AiErrorRules
{
    public static ErrorRule HttpOperation { get; } = ErrorRule.For<HttpOperationException>(Describe);

    public static void Register(IErrorTranslator translator) => translator.AddRule(HttpOperation);

    private static UserFacingError Describe(HttpOperationException exception) =>
        exception.StatusCode switch
        {
            HttpStatusCode.ServiceUnavailable =>
                new("Model servisi su anda kullanilamiyor (503). Biraz sonra tekrar deneyin veya model/endpoint ayarlarini kontrol edin.", ErrorCategory.ExternalService),
            HttpStatusCode.TooManyRequests =>
                new("Model servisi hiz limitine takildi (429). Biraz sonra tekrar deneyin veya kota/limit ayarlarini kontrol edin.", ErrorCategory.RateLimit),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new($"Model servisi kimlik dogrulamayi reddetti ({(int)exception.StatusCode.Value}). API anahtari ve credential ayarlarini kontrol edin.", ErrorCategory.Authentication),
            HttpStatusCode.NotFound =>
                new("Model servisi secilen modeli veya endpoint'i bulamadi (404). Model/deployment adini kontrol edin.", ErrorCategory.Configuration),
            { } code =>
                new($"Model servisi istegi basarisiz oldu ({(int)code}).", ErrorCategory.ExternalService),
            null =>
                new("Model servisi istegi basarisiz oldu.", ErrorCategory.ExternalService)
        };
}
