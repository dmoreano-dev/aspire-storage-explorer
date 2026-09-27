namespace StorageExplorer.Web;

/// <summary>
/// Whether the explorer runs inside a container. It is behind an interface so that what depends on it can be tested
/// both ways: it reads an environment variable, which is the same for the whole process.
/// </summary>
internal interface IContainerEnvironment
{
    /// <summary><c>true</c> when the loopback host is the container itself and not the machine that runs it.</summary>
    bool RunningInContainer { get; }
}

internal sealed class ContainerEnvironment : IContainerEnvironment
{
    /// <summary>The .NET container images set this variable; outside a container the loopback host is the real machine.</summary>
    public bool RunningInContainer =>
        string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
}
