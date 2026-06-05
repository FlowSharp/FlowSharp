namespace FlowSharp.Application.Workflows;

/// <summary>
/// appsettings.json "RateLimit" bolumu. Kullanici (workflow sahibi) basina dakikalik calistirma
/// limitini yonetir. Paylasimli SaaS'ta tek bir kiracinin worker havuzunu doldurmasini onler.
/// Admin sahipli workflow'lar limitten muaftir.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>Limit etkin mi? Kapatilirsa hicbir kontrol uygulanmaz.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Kullanici basina bir dakikada izin verilen azami workflow calistirma sayisi.</summary>
    public int RunsPerMinutePerUser { get; set; } = 60;
}
