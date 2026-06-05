namespace FlowSharp.Application.Identity;

/// <summary>
/// Kullanici kimliklerini gosterilebilir etiketlere (e-posta) cozer. Admin gorunumlerinde
/// kayit sahiplerini okunabilir gostermek icindir; sahiplik kuralinin kendisi degil, yalniz
/// gosterim icin kullanilir.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Verilen kullanici Id'leri icin e-posta (yoksa kullanici adi, o da yoksa Id) eslemesi doner.</summary>
    Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(
        IEnumerable<string> userIds, CancellationToken cancellationToken = default);
}
