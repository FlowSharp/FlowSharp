namespace FlowSharp.Infrastructure.Security;

public sealed class HttpNodeNetworkOptions
{
    public const string SectionName = "HttpNodes";

    public string Exposure { get; set; } = "Local";

    public bool BlockPrivateNetworks { get; set; }

    public bool ShouldBlockPrivateNetworks =>
        BlockPrivateNetworks || Exposure.Equals("Public", StringComparison.OrdinalIgnoreCase);
}
