using System.Net;
using Microsoft.Extensions.Options;

namespace FlowSharp.Infrastructure.Security;

public sealed class PrivateNetworkBlockingHandler(IOptionsMonitor<HttpNodeNetworkOptions> options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!options.CurrentValue.ShouldBlockPrivateNetworks)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var uri = request.RequestUri ?? throw new InvalidOperationException("HTTP istegi icin URL gerekli.");
        await EnsurePublicTargetAsync(uri, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private static async Task EnsurePublicTargetAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Public modda yalniz HTTP ve HTTPS hedefleri desteklenir.");
        }

        var host = uri.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            ThrowIfPrivate(uri, literalAddress);
            return;
        }

        var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"'{uri.Host}' icin DNS kaydi bulunamadi.");
        }

        foreach (var address in addresses)
        {
            ThrowIfPrivate(uri, address);
        }
    }

    private static void ThrowIfPrivate(Uri uri, IPAddress address)
    {
        if (IsBlockedAddress(address))
        {
            throw new InvalidOperationException($"Public modda private/localhost hedeflerine HTTP istegi engellendi: {uri.Host}");
        }
    }

    private static bool IsBlockedAddress(IPAddress address)
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
