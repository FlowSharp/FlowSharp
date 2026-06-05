namespace FlowSharp.Application.Security;

/// <summary>
/// Bir islemi yapan aktorun kimligi ve yetki kapsami. Sahiplik (owner) tabanli izolasyon
/// icin tek noktadan kullanilir: Admin tum kayitlari gorur/yonetir, digerleri yalniz kendi
/// <see cref="UserId"/>'sine ait kayitlari. UI (cookie) ve API (token) bu tipi farkli
/// kaynaklardan doldurur; is mantigi her iki cagiranda da ayni kalir.
/// </summary>
public readonly record struct ActorScope(string? UserId, bool IsAdmin)
{
    /// <summary>
    /// Sorgu/komutlara verilecek sahiplik filtresi: Admin ise kisit yok (<c>null</c>),
    /// degilse yalniz aktore ait kayitlar (<see cref="UserId"/>).
    /// </summary>
    public string? OwnerFilter => IsAdmin ? null : UserId;
}
