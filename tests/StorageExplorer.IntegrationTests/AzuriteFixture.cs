extern alias TestAppHost;

using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// One Azurite for the whole run, started by Aspire the way an AppHost would. Needs Docker. Tests keep out of each
/// other's way by working in containers of their own (see <see cref="TestContainer"/>).
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    // Public, well-known credentials of the emulator (not a secret).
    private const string AccountName = "devstoreaccount1";
    private const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    // Pulling the image the first time can take a while.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> shared = new();

    private DistributedApplication? app;

    /// <summary>
    /// Connection string for the emulator, reachable from this process. It uses <c>127.0.0.1</c> and not the
    /// <c>localhost</c> that Aspire gives: see <see cref="HostnameConnectionString"/>.
    /// </summary>
    public string ConnectionString { get; private set; } = "";

    /// <summary>
    /// The same emulator through the name <c>localhost</c>. Aspire gives the emulator a random port, and the Azure SDK
    /// builds the address of a blob without its container when the host is a name and the port is not 10000 to 10002.
    /// </summary>
    public string HostnameConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        using var cancellation = new CancellationTokenSource(StartupTimeout);
        var cancellationToken = cancellation.Token;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<TestAppHost::Projects.StorageExplorer_TestAppHost>(cancellationToken);
        app = await appHost.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("storage", cancellationToken);

        var port = app.GetEndpoint("storage", "blob").Port;
        ConnectionString = BuildConnectionString("127.0.0.1", port);
        HostnameConnectionString = BuildConnectionString("localhost", port);

        await WaitUntilAnswering(cancellationToken);
    }

    /// <summary>
    /// Builds something expensive once for the whole run, the first time a test asks for it. xunit creates a new
    /// instance of a test class for every test, so a class cannot keep it in its own fields.
    /// </summary>
    public async Task<T> SharedAsync<T>(string key, Func<Task<T>> create) where T : notnull =>
        (T)await shared.GetOrAdd(key, _ => new Lazy<Task<object>>(async () => await create())).Value;

    public async Task DisposeAsync()
    {
        if (app is not null)
            await app.DisposeAsync();
    }

    /// <summary>A connection string for the emulator account at the given host and port.</summary>
    public static string BuildConnectionString(string host, int port) =>
        $"DefaultEndpointsProtocol=http;AccountName={AccountName};AccountKey={AccountKey};" +
        $"BlobEndpoint=http://{host}:{port}/{AccountName};";

    // The container can be running before Azurite accepts requests.
    private async Task WaitUntilAnswering(CancellationToken cancellationToken)
    {
        var client = new BlobServiceClient(ConnectionString);

        while (true)
        {
            try
            {
                await client.GetPropertiesAsync(cancellationToken);
                return;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzuriteCollection : ICollectionFixture<AzuriteFixture>
{
    public const string Name = "Azurite";
}
