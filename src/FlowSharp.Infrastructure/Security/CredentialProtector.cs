using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FlowSharp.Infrastructure.Security;

/// <summary>Credential govdelerini sifreler/cozer. Hem Web hem Worker ayni anahtari kullanir.</summary>
public interface ICredentialProtector
{
    string Encrypt(string plaintext);

    string Decrypt(string payload);
}

/// <summary>
/// AES-GCM tabanli sifreleme. Anahtar yapilandirmadan okunur:
/// <c>Security:CredentialEncryptionKey</c> (base64, 32 bayt). Ayarlanmazsa
/// geliştirme icin sabit bir anahtar turetilir (production'da uyari verir).
/// </summary>
public sealed class CredentialProtector : ICredentialProtector
{
    private readonly byte[] key;

    public CredentialProtector(IConfiguration configuration, ILogger<CredentialProtector> logger)
    {
        var configured = configuration["Security:CredentialEncryptionKey"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            key = Convert.FromBase64String(configured);
            if (key.Length != 32)
            {
                throw new InvalidOperationException("Security:CredentialEncryptionKey base64 cozumlendiginde 32 bayt olmali.");
            }
        }
        else
        {
            logger.LogWarning("Security:CredentialEncryptionKey ayarlanmamis; gelistirme icin turetilmis sabit anahtar kullaniliyor. Production'da mutlaka ayarlayin.");
            key = SHA256.HashData(Encoding.UTF8.GetBytes("FlowSharp-dev-credential-key"));
        }
    }

    public string Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, combined, nonce.Length + tag.Length, cipher.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string payload)
    {
        var combined = Convert.FromBase64String(payload);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = combined.AsSpan(0, nonceSize);
        var tag = combined.AsSpan(nonceSize, tagSize);
        var cipher = combined.AsSpan(nonceSize + tagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, tagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
