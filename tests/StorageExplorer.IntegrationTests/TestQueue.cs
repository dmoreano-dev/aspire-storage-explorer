using Azure.Storage.Queues;
using Microsoft.Extensions.Options;
using StorageExplorer.Web.Queues;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// A queue that exists for one test class: created empty, filled by the test, deleted at the end. The name is random,
/// so tests never see each other's messages.
/// </summary>
public sealed class TestQueue : IAsyncDisposable
{
    private TestQueue(string name, QueueClient client, string connectionString)
    {
        Name = name;
        Client = client;
        ConnectionString = connectionString;
    }

    public QueueClient Client { get; }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TestQueue> CreateAsync(AzuriteFixture azurite)
    {
        var name = $"t{Guid.NewGuid():N}";
        var client = new QueueServiceClient(azurite.ConnectionString).GetQueueClient(name);
        await client.CreateAsync();

        return new TestQueue(name, client, azurite.ConnectionString);
    }

    /// <summary>The service under test, on the real connection object, the way the app builds them.</summary>
    internal QueueExplorerService Service(string? connectionString = null) =>
        new(new StorageConnection(Options.Create(new StorageExplorerOptions
        {
            ConnectionString = connectionString ?? ConnectionString,
        })));

    public Task SendAsync(string text) => Client.SendMessageAsync(text);

    public async ValueTask DisposeAsync() => await Client.DeleteIfExistsAsync();
}
