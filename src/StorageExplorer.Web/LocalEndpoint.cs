using System.Net;

namespace StorageExplorer.Web;

/// <summary>
/// Tells an endpoint on this machine (or on its container network) from a remote account. It fails safe: a host it does
/// not recognize counts as remote, and a real Azure account never has a loopback address or a host without a dot.
/// </summary>
internal static class LocalEndpoint
{
    public static bool IsLocal(Uri endpoint)
    {
        var host = endpoint.DnsSafeHost;

        if (endpoint.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

        // localhost, or the name of a container on the Aspire network (for example "storage"), has no dot.
        return !host.Contains('.')
               || host.Equals(LoopbackHostRewriter.HostGateway, StringComparison.OrdinalIgnoreCase)
               || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }
}
