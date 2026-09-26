using System.Text.RegularExpressions;

namespace StorageExplorer.Web;

/// <summary>
/// Inside a container <c>localhost</c> is the container itself, but an emulator or a desktop tool usually runs on the
/// host machine. This maps the loopback hosts of a connection string to the host, so the same connection string
/// used from the host works here.
/// </summary>
internal static partial class LoopbackHostRewriter
{
    internal const string HostGateway = "host.docker.internal";

    // What UseDevelopmentStorage=true stands for, with the emulator's default ports.
    private const string DevelopmentStorage =
        "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;" +
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;" +
        $"BlobEndpoint=http://{HostGateway}:10000/devstoreaccount1;" +
        $"QueueEndpoint=http://{HostGateway}:10001/devstoreaccount1;" +
        $"TableEndpoint=http://{HostGateway}:10002/devstoreaccount1;";

    /// <summary>The .NET container images set this variable; outside a container the loopback host is the real machine.</summary>
    public static bool RunningInContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);

    public static string Rewrite(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
            return connectionString;

        return DevelopmentStorageShortcut().IsMatch(connectionString) ? DevelopmentStorage : LoopbackHost().Replace(connectionString, HostGateway);
    }

    // The host right after the "//" of an endpoint URI: http://127.0.0.1:10000/... or http://localhost/...
    [GeneratedRegex(@"(?<=//)(localhost|127\.0\.0\.1|\[::1\])(?=[:/;]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LoopbackHost();

    [GeneratedRegex(@"(^|;)\s*UseDevelopmentStorage\s*=\s*true\s*(;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DevelopmentStorageShortcut();
}
