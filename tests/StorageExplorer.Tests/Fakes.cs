using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace StorageExplorer.Web.Tests;

internal static class TestConnectionStrings
{
    public const string EmulatorKey =
        "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>Points at a port nobody listens on: building a client never connects, and a test that does fails fast.</summary>
    public static readonly string Local =
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={EmulatorKey};" +
        "BlobEndpoint=http://127.0.0.1:1/devstoreaccount1;";

    /// <summary>
    /// A remote account. The host ends in <c>.invalid</c>, which never resolves (RFC 2606), so a test that reaches
    /// for the network fails instead of sending a request to a real storage account.
    /// </summary>
    public static readonly string Remote =
        "DefaultEndpointsProtocol=https;AccountName=prodaccount;AccountKey=c2VjcmV0LWtleS1kby1ub3QtbGVhaw==;" +
        "BlobEndpoint=https://prodaccount.blob.example.invalid;";

    /// <summary>The host Aspire gives to a container that another one reaches: <c>{name}.dev.internal</c>.</summary>
    public static readonly string DevInternal =
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={EmulatorKey};" +
        "BlobEndpoint=http://storage.dev.internal:10000/devstoreaccount1;";
}

/// <summary>Says whether the explorer runs in a container, whatever the environment of the machine that runs the tests.</summary>
internal sealed class FakeContainerEnvironment(bool runningInContainer) : IContainerEnvironment
{
    public bool RunningInContainer => runningInContainer;
}

internal static class ContainerEnvironmentTestExtensions
{
    /// <summary>Replaces the check of the environment, so the app behaves as inside or outside a container.</summary>
    public static IWebHostBuilder UseContainerEnvironment(this IWebHostBuilder builder, bool runningInContainer) =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IContainerEnvironment>();
            services.AddSingleton<IContainerEnvironment>(new FakeContainerEnvironment(runningInContainer));
        });
}

/// <summary>Keeps what the app logs, so a test can say what was logged and how many times.</summary>
internal sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<(string Category, string Message)> entries = [];

    public IReadOnlyList<(string Category, string Message)> Entries
    {
        get
        {
            lock (entries)
                return [.. entries];
        }
    }

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class CollectingLogger(string category, List<(string Category, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
                entries.Add((category, formatter(state, exception)));
        }
    }
}

/// <summary>A connection that records what the endpoints ask of it and never touches the network.</summary>
internal sealed class FakeStorageConnection : IStorageConnection
{
    public BlobServiceClient Client { get; } = BlobClientFactory.Create(TestConnectionStrings.Local);

    public ConnectionInfo Info { get; set; } = Local;

    /// <summary>When set, <see cref="UseAsync"/> throws it, like a connection that cannot be reached.</summary>
    public Exception? UseFails { get; set; }

    public List<(BlobServiceClient Client, bool AllowWrites)> Used { get; } = [];

    public int ResetCount { get; private set; }

    public static ConnectionInfo Local { get; } =
        new("devstoreaccount1", "127.0.0.1:1", IsCustom: false, IsLocal: true, ReadOnly: false, ReadOnlyLocked: false);

    /// <summary>A remote account that the page could still allow writes on.</summary>
    public static ConnectionInfo RemoteReadOnly { get; } =
        new("prodaccount", "prodaccount.blob.example.invalid", IsCustom: false, IsLocal: false, ReadOnly: true, ReadOnlyLocked: false);

    /// <summary>Locked by <c>readOnly: true</c> in the AppHost.</summary>
    public static ConnectionInfo Locked { get; } =
        new("devstoreaccount1", "127.0.0.1:1", IsCustom: false, IsLocal: true, ReadOnly: true, ReadOnlyLocked: true);

    public Task UseAsync(BlobServiceClient client, bool allowWrites, CancellationToken cancellationToken)
    {
        if (UseFails is not null)
            throw UseFails;

        Used.Add((client, allowWrites));
        return Task.CompletedTask;
    }

    public void Reset() => ResetCount++;
}

/// <summary>An explorer service that returns what the test sets and records how it was called.</summary>
internal sealed class FakeBlobExplorerService : IBlobExplorerService
{
    public List<string> Calls { get; } = [];

    /// <summary>When set, every call throws it.</summary>
    public Exception? Fails { get; set; }

    public IReadOnlyList<ContainerSummary> Containers { get; set; } = [];

    public EntryListing Listing { get; set; } = new([], Truncated: false);

    public BlobDownload? Download { get; set; }

    public bool DeleteResult { get; set; } = true;

    public Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken) =>
        Record("containers", Containers);

    public Task<EntryListing> ListEntriesAsync(string container, string? prefix, CancellationToken cancellationToken) =>
        Record($"entries:{container}:{prefix}", Listing);

    public Task<EntryListing> SearchAsync(string container, string? prefix, string term, CancellationToken cancellationToken) =>
        Record($"search:{container}:{prefix}:{term}", Listing);

    public Task<BlobDownload?> DownloadAsync(string container, string path, CancellationToken cancellationToken) =>
        Record($"download:{container}:{path}", Download);

    public Task<bool> DeleteAsync(string container, string path, CancellationToken cancellationToken) =>
        Record($"delete:{container}:{path}", DeleteResult);

    private Task<T> Record<T>(string call, T result)
    {
        Calls.Add(call);

        return Fails is null ? Task.FromResult(result) : Task.FromException<T>(Fails);
    }
}

/// <summary>
/// The whole web app in memory, with the storage side replaced by fakes, so the routes, the filters and the error
/// handling run for real without an account.
/// </summary>
internal sealed class ApiHost : IDisposable
{
    private readonly WebApplicationFactory<Program> factory;

    /// <param name="connection">What the fake connection says about the account.</param>
    /// <param name="configure">Anything else the test needs to change in the host.</param>
    /// <param name="runningInContainer">
    /// Whether the app thinks it runs in a container. It is never left to the machine that runs the tests, which could
    /// be a container itself.
    /// </param>
    public ApiHost(ConnectionInfo? connection = null, Action<IWebHostBuilder>? configure = null, bool runningInContainer = false)
    {
        Connection = new FakeStorageConnection { Info = connection ?? FakeStorageConnection.Local };

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The image does not set ASPNETCORE_ENVIRONMENT, so it runs as Production. In Development a request with a
            // missing parameter throws instead of answering 400.
            builder.UseEnvironment("Production");

            // Startup validates this, but it never connects: the fakes below are what the endpoints use.
            builder.UseSetting("StorageExplorer:ConnectionString", TestConnectionStrings.Local);

            builder.UseContainerEnvironment(runningInContainer);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IStorageConnection>();
                services.RemoveAll<IBlobExplorerService>();
                services.AddSingleton<IStorageConnection>(Connection);
                services.AddSingleton<IBlobExplorerService>(Explorer);
            });

            configure?.Invoke(builder);
        });

        Client = factory.CreateClient();
    }

    public FakeStorageConnection Connection { get; }

    public FakeBlobExplorerService Explorer { get; } = new();

    public HttpClient Client { get; }

    public IServiceProvider Services => factory.Services;

    public HttpRequestMessage Request(HttpMethod method, string uri, bool explorerHeader = true, string? origin = null)
    {
        var request = new HttpRequestMessage(method, uri);

        if (explorerHeader)
            request.Headers.Add("X-Storage-Explorer", "1");

        if (origin is not null)
            request.Headers.Add("Origin", origin);

        return request;
    }

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
    }
}
