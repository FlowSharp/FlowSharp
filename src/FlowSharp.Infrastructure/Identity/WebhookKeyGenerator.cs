using System.Security.Cryptography;

namespace FlowSharp.Infrastructure.Identity;

/// <summary>
/// URL-guvenli webhook "kilit" anahtari uretir. Anahtarlar workflow registration'larinda
/// workflow'a ozel olarak kullanilir; ayni path'i kullanan workflow'lar boylece cakismaz.
/// </summary>
public static class WebhookKeyGenerator
{
    // Karistirici karakterler (0/O, 1/I/L) cikarilmis Crockford base32 alfabesi.
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Generate(int length = 16)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        return string.Create(length, bytes.ToArray(), static (chars, source) =>
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = Alphabet[source[i] % Alphabet.Length];
            }
        });
    }
}
