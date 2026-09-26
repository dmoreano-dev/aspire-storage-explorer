using System.Text.RegularExpressions;

namespace StorageExplorer.Web;

/// <summary>
/// Maps the hosts of a connection string that do not work from inside the explorer container.
/// <list type="bullet">
/// <item>Inside a container <c>localhost</c> is the container itself, but an emulator or a desktop tool usually runs on
/// the host machine, so the loopback hosts become the host, and the same connection string used from the host works here.</item>
/// <item>Aspire reaches a container from another one as <c>{name}.dev.internal</c>. Azurite reads a host with a dot as
/// <c>{account}.blob...</c> and looks for an account called <c>{name}</c>, which fails with an empty 400. The plain
/// <c>{name}</c> is also a network alias of that container, and Azurite reads the account from the path for it.</item>
/// </list>
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

        if (DevelopmentStorageShortcut().IsMatch(connectionString))
            return DevelopmentStorage;

        return DevInternalHost().Replace(LoopbackHost().Replace(connectionString, HostGateway), "$1");
    }

    // The host right after the "//" of an endpoint URI: http://127.0.0.1:10000/... or http://localhost/...
    [GeneratedRegex(@"(?<=//)(localhost|127\.0\.0\.1|\[::1\])(?=[:/;]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LoopbackHost();

    // The host right after the "//" that Aspire gives to containers, http://storage.dev.internal:10000/...: the name is the group.
    [GeneratedRegex(@"(?<=//)([A-Za-z0-9-]+)\.dev\.internal(?=[:/;]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DevInternalHost();

    [GeneratedRegex(@"(^|;)\s*UseDevelopmentStorage\s*=\s*true\s*(;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DevelopmentStorageShortcut();
}
