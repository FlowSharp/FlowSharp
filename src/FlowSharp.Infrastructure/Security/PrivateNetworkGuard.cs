using System.Net;

namespace FlowSharp.Infrastructure.Security;

/// <summary>
/// Bir IP adresinin private/localhost/reserved (SSRF acisindan riskli) olup olmadigini
/// belirleyen ortak kurallar. Hem <see cref="PrivateNetworkBlockingHandler"/> (erken kontrol)
/// hem de cikis HTTP istemcisinin <c>ConnectCallback</c>'i (gercek baglanti aninda, IP pinleyerek)
/// bu mantigi kullanir; boylece DNS rebinding ve yonlendirme (redirect) ile atlatma engellenir.
/// </summary>
internal static class PrivateNetworkGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork => IsBlockedIPv4(bytes),
            System.Net.Sockets.AddressFamily.InterNetworkV6 => IsBlockedIPv6(bytes),
            _ => true
        };
    }

    /// <summary>
    /// IPv4 gomulu IPv6 formlarinda (NAT64 64:ff9b::/96, 6to4 2002::/16, IPv4-compatible ::/96)
    /// gercek hedef gomulu IPv4'tur. Bu adresler IPv4 kontrollerini atlayabildiginden gomulu
    /// IPv4 cikarilip tekrar IPv4 kurallarindan gecirilir (SSRF savunma derinligi).
    /// </summary>
    private static byte[]? ExtractEmbeddedIPv4(byte[] bytes)
    {
        // NAT64 well-known prefix: 64:ff9b:: -> son 4 bayt IPv4.
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
        {
            return [bytes[12], bytes[13], bytes[14], bytes[15]];
        }

        // 6to4: 2002:AABB:CCDD:: -> bayt 2..5 IPv4 (AA.BB.CC.DD).
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
        {
            return [bytes[2], bytes[3], bytes[4], bytes[5]];
        }

        // IPv4-compatible (deprecated): ilk 12 bayt 0, son 4 bayt IPv4. ::1 (loopback) ve ::
        // (unspecified) baska yerde ele alindigindan son baytlardan en az biri sifirdan farkli olmali.
        if (bytes.Take(12).All(b => b == 0) && (bytes[12] | bytes[13] | bytes[14]) != 0)
        {
            return [bytes[12], bytes[13], bytes[14], bytes[15]];
        }

        return null;
    }

    private static bool IsBlockedIPv4(byte[] bytes)
    {
        var first = bytes[0];
        var second = bytes[1];

        return first switch
        {
            0 => true,
            10 => true,
            100 when second is >= 64 and <= 127 => true,
            127 => true,
            169 when second == 254 => true,
            172 when second is >= 16 and <= 31 => true,
            192 when second == 168 => true,
            >= 224 => true,
            _ => false
        };
    }

    private static bool IsBlockedIPv6(byte[] bytes)
    {
        // IPv4 gomulu formlarda (NAT64/6to4/IPv4-compatible) gercek hedefi IPv4 olarak degerlendir.
        if (ExtractEmbeddedIPv4(bytes) is { } embedded)
        {
            return IsBlockedIPv4(embedded);
        }

        return bytes[0] switch
        {
            0xFC or 0xFD => true,
            0xFE when (bytes[1] & 0xC0) == 0x80 => true,
            0xFF => true,
            _ => IsUnspecifiedIPv6(bytes)
        };
    }

    private static bool IsUnspecifiedIPv6(byte[] bytes) => bytes.All(b => b == 0);
}
