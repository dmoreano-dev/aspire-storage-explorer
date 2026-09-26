namespace Aspire.Hosting.ApplicationModel;

/// <summary>A container resource running the Storage Explorer web UI.</summary>
/// <param name="name">The resource name.</param>
public sealed class StorageExplorerResource(string name) : ContainerResource(name)
{
    internal const string HttpEndpointName = "http";
    internal const int ContainerPort = 8080;

    /// <summary>Configuration key (as an environment variable) the web app reads its connection string from.</summary>
    internal const string ConnectionStringVariable = "StorageExplorer__ConnectionString";
}
