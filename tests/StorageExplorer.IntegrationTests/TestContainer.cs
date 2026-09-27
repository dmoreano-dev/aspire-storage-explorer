using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// A container that exists for one test class: created empty, filled by the test, deleted at the end. The name is
/// random, so tests never see each other's blobs.
/// </summary>
public sealed class TestContainer : IAsyncDisposable
{
    private TestContainer(string name, BlobContainerClient client, string connectionString)
    {
        Name = name;
        Client = client;
        ConnectionString = connectionString;
    }

    public BlobContainerClient Client { get; }

    // Kept here on purpose: with a host that is a name, the SDK reads the account segment of the path as the name of
    // the container, so BlobContainerClient.Name cannot be trusted with the emulator.
    public string Name { get; }

    public string ConnectionString { get; }

    public static async Task<TestContainer> CreateAsync(AzuriteFixture azurite)
    {
        var name = $"t{Guid.NewGuid():N}";
        var client = new BlobServiceClient(azurite.ConnectionString).GetBlobContainerClient(name);
        await client.CreateAsync();

        return new TestContainer(name, client, azurite.ConnectionString);
    }

    /// <summary>The service under test, on the real connection object, the way the app builds them.</summary>
    internal BlobExplorerService Service(bool? readOnly = null, string? connectionString = null) =>
        new(new StorageConnection(Options.Create(new StorageExplorerOptions
        {
            ConnectionString = connectionString ?? ConnectionString,
            ReadOnly = readOnly,
        })));

    public async Task Upload(string name, string content = "content", string? contentType = null)
    {
        var options = new BlobUploadOptions
        {
            HttpHeaders = contentType is null ? null : new BlobHttpHeaders { ContentType = contentType },
        };

        await Client.GetBlobClient(name).UploadAsync(BinaryData.FromString(content), options);
    }

    public async Task<bool> Exists(string name) => await Client.GetBlobClient(name).ExistsAsync();

    public async ValueTask DisposeAsync() => await Client.DeleteIfExistsAsync();
}
