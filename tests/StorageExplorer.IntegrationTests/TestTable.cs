using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using StorageExplorer.Web.Tables;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// A table that exists for one test class: created empty, filled by the test, deleted at the end. The name is random,
/// so tests never see each other's entities.
/// </summary>
public sealed class TestTable : IAsyncDisposable
{
    private TestTable(string name, TableClient client, string connectionString)
    {
        Name = name;
        Client = client;
        ConnectionString = connectionString;
    }

    public TableClient Client { get; }

    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TestTable> CreateAsync(AzuriteFixture azurite)
    {
        var name = $"t{Guid.NewGuid():N}";
        var client = new TableServiceClient(azurite.ConnectionString).GetTableClient(name);
        await client.CreateAsync();

        return new TestTable(name, client, azurite.ConnectionString);
    }

    /// <summary>The service under test, on the real connection object, the way the app builds them.</summary>
    internal TableExplorerService Service(string? connectionString = null) =>
        new(new StorageConnection(Options.Create(new StorageExplorerOptions
        {
            ConnectionString = connectionString ?? ConnectionString,
        })));

    public Task AddAsync(TableEntity entity) => Client.AddEntityAsync(entity);

    public async ValueTask DisposeAsync() => await Client.DeleteAsync();
}
