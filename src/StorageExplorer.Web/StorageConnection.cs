using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web;

/// <summary>What the UI may know about the active connection. It never carries the account key.</summary>
/// <param name="AccountName">The storage account name.</param>
/// <param name="Endpoint">Host and port of the blob endpoint, without path or query.</param>
/// <param name="IsCustom"><c>true</c> when it was set from the UI instead of the AppHost.</param>
internal sealed record ConnectionInfo(string AccountName, string Endpoint, bool IsCustom);

/// <summary>The storage account the explorer is browsing. It can be swapped at runtime.</summary>
internal interface IStorageConnection
{
    BlobServiceClient Client { get; }

    ConnectionInfo Info { get; }

    /// <summary>
    /// Checks that the client can list the containers, then makes it the active connection.
    /// When the check fails it throws and the active connection is left as it was.
    /// </summary>
    Task UseAsync(BlobServiceClient client, CancellationToken cancellationToken);

    /// <summary>Goes back to the connection string the explorer was started with.</summary>
    void Reset();
}

/// <summary>
/// Keeps the active connection in memory only: a connection set from the UI is lost when the explorer restarts.
/// </summary>
internal sealed class StorageConnection : IStorageConnection
{
    private sealed record Active(BlobServiceClient Client, bool IsCustom);

    private readonly Active defaultConnection;
    private volatile Active current;

    public StorageConnection(IOptions<StorageExplorerOptions> options)
    {
        defaultConnection = new Active(BlobClientFactory.Create(options.Value.ConnectionString), IsCustom: false);
        current = defaultConnection;
    }

    public BlobServiceClient Client => current.Client;

    public ConnectionInfo Info
    {
        get
        {
            var active = current;
            return new ConnectionInfo(active.Client.AccountName, active.Client.Uri.Authority, active.IsCustom);
        }
    }

    public async Task UseAsync(BlobServiceClient client, CancellationToken cancellationToken)
    {
        // One page of one container is enough to prove the endpoint is reachable and the credentials are accepted.
        await foreach (var _ in client.GetBlobContainersAsync(cancellationToken: cancellationToken).AsPages(pageSizeHint: 1))
            break;

        current = new Active(client, IsCustom: true);
    }

    public void Reset() => current = defaultConnection;
}
