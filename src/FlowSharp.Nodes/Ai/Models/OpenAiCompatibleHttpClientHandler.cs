using System.Net.Http;

namespace FlowSharp.Nodes.Ai.Models;

/// <summary>
/// OpenAI isteklerini ilgili uyumlu API adresine yonlendiren delegating handler.
/// </summary>
public sealed class OpenAiCompatibleHttpClientHandler : DelegatingHandler
{
    private readonly string _baseUrl;

    public OpenAiCompatibleHttpClientHandler(string baseUrl)
    {
        _baseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
        InnerHandler = new HttpClientHandler();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var relativePath = request.RequestUri!.PathAndQuery.TrimStart('/');
        if (_baseUrl.EndsWith("/v1/") && relativePath.StartsWith("v1/"))
        {
            relativePath = relativePath.Substring(3);
        }

        request.RequestUri = new Uri(new Uri(_baseUrl), relativePath);
        return await base.SendAsync(request, cancellationToken);
    }
}
