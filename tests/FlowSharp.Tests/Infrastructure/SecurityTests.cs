using System.Net;
using System.Net.Http;
using FluentAssertions;
using FlowSharp.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace FlowSharp.Tests.Infrastructure;

public class CredentialProtectorTests
{
    private static CredentialProtector Protector()
    {
        var key = Convert.ToBase64String(new byte[32]); // 32 sifir bayt -> gecerli uzunluk
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:CredentialEncryptionKey"] = key })
            .Build();
        return new CredentialProtector(config);
    }

    [Fact]
    public void Encrypt_then_decrypt_round_trips()
    {
        var protector = Protector();
        var secret = "sk-12345-çok-gizli";

        var cipher = protector.Encrypt(secret);
        cipher.Should().NotBe(secret);
        protector.Decrypt(cipher).Should().Be(secret);
    }

    [Fact]
    public void Encrypt_produces_different_ciphertext_each_time_random_nonce()
    {
        var protector = Protector();
        protector.Encrypt("ayni").Should().NotBe(protector.Encrypt("ayni"));
    }

    [Fact]
    public void Missing_key_throws()
    {
        var config = new ConfigurationBuilder().Build();
        var act = () => new CredentialProtector(config);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Wrong_length_key_throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:CredentialEncryptionKey"] = Convert.ToBase64String(new byte[16])
            }).Build();
        var act = () => new CredentialProtector(config);
        act.Should().Throw<InvalidOperationException>();
    }
}

public class PrivateNetworkBlockingHandlerTests
{
    private static HttpClient Client(bool block)
    {
        var options = Options.Create(new HttpNodeNetworkOptions { BlockPrivateNetworks = block });
        var handler = new PrivateNetworkBlockingHandler(new StaticOptionsMonitor(options.Value))
        {
            InnerHandler = new StubHandler()
        };
        return new HttpClient(handler);
    }

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://169.254.0.1/")]
    public async Task Blocks_private_and_loopback_literals(string url)
    {
        var act = async () => await Client(block: true).GetAsync(url);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Allows_public_literal_when_blocking_enabled()
    {
        var response = await Client(block: true).GetAsync("http://8.8.8.8/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Allows_private_when_blocking_disabled()
    {
        var response = await Client(block: false).GetAsync("http://127.0.0.1/");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://[::1]/")]                  // IPv6 loopback
    [InlineData("http://[fc00::1]/")]              // IPv6 unique-local
    [InlineData("http://[fe80::1]/")]              // IPv6 link-local
    [InlineData("http://[64:ff9b::7f00:1]/")]      // NAT64 -> 127.0.0.1
    [InlineData("http://[64:ff9b::a9fe:a9fe]/")]   // NAT64 -> 169.254.169.254 (bulut metadata)
    [InlineData("http://[2002:7f00:1::]/")]        // 6to4 -> 127.0.0.1
    [InlineData("http://[::10.0.0.5]/")]           // IPv4-compatible -> 10.0.0.5
    public async Task Blocks_private_ipv6_literals(string url)
    {
        var act = async () => await Client(block: true).GetAsync(url);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("http://[64:ff9b::808:808]/")]     // NAT64 -> 8.8.8.8 (public)
    [InlineData("http://[2002:808:808::]/")]       // 6to4 -> 8.8.8.8 (public)
    public async Task Allows_public_embedded_ipv4_ipv6_literals(string url)
    {
        var response = await Client(block: true).GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://0.0.0.0/")]          // unspecified
    [InlineData("http://100.64.0.1/")]       // carrier-grade NAT
    [InlineData("http://224.0.0.1/")]        // multicast
    public async Task Blocks_additional_ipv4_reserved_ranges(string url)
    {
        var act = async () => await Client(block: true).GetAsync(url);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Non_http_scheme_is_rejected_in_block_mode()
    {
        var act = async () => await Client(block: true).GetAsync("ftp://example.com/");
        await act.Should().ThrowAsync<Exception>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class StaticOptionsMonitor(HttpNodeNetworkOptions value) : IOptionsMonitor<HttpNodeNetworkOptions>
    {
        public HttpNodeNetworkOptions CurrentValue => value;
        public HttpNodeNetworkOptions Get(string? name) => value;
        public IDisposable OnChange(Action<HttpNodeNetworkOptions, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
