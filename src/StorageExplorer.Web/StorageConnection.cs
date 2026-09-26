using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web;

/// <summary>What the UI may know about the active connection. It never carries the account key.</summary>
/// <param name="AccountName">The storage account name.</param>
/// <param name="Endpoint">Host and port of the blob endpoint, without path or query.</param>
/// <param name="IsCustom"><c>true</c> when it was set from the UI instead of the AppHost.</param>
/// <param name="IsLocal"><c>true</c> when the endpoint is on this machine, <c>false</c> for a remote account.</param>
/// <param name="ReadOnly"><c>true</c> when the explorer refuses to change data on this connection.</param>
/// <param name="ReadOnlyLocked"><c>true</c> when the AppHost set <c>readOnly: true</c>, so the page cannot allow writes.</param>
internal sealed record ConnectionInfo(
    string AccountName,
    string Endpoint,
    bool IsCustom,
    bool IsLocal,
    bool ReadOnly,
    bool ReadOnlyLocked);

/// <summary>The storage account the explorer is browsing. It can be swapped at runtime.</summary>
internal interface IStorageConnection
{
    BlobServiceClient Client { get; }

    ConnectionInfo Info { get; }

    /// <summary>
    /// Checks that the client can list the containers, then makes it the active connection.
    /// When the check fails it throws and the active connection is left as it was.
    /// </summary>
    /// <param name="client">The client of the new connection.</param>
    /// <param name="allowWrites">Lets the explorer change data when the connection is remote.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UseAsync(BlobServiceClient client, bool allowWrites, CancellationToken cancellationToken);

    /// <summary>Goes back to the connection string the explorer was started with.</summary>
    void Reset();
}

/// <summary>
/// Keeps the active connection in memory only: a connection set from the UI is lost when the explorer restarts.
/// </summary>
internal sealed class StorageConnection : IStorageConnection
{
    private sealed record Active(BlobServiceClient Client, bool IsCustom, bool IsLocal, bool ReadOnly);

    private readonly bool? readOnlyOption;
    private readonly Active defaultConnection;
    private volatile Active current;

    public StorageConnection(IOptions<StorageExplorerOptions> options)
    {
        readOnlyOption = options.Value.ReadOnly;

        var client = BlobClientFactory.Create(options.Value.ConnectionString);
        var isLocal = LocalEndpoint.IsLocal(client.Uri);

        // The AppHost decides for its own connection; without a choice only a local one is writable.
        defaultConnection = new Active(client, IsCustom: false, isLocal, ReadOnly: readOnlyOption ?? !isLocal);
        current = defaultConnection;
    }

    public BlobServiceClient Client => current.Client;

    public ConnectionInfo Info
    {
        get
        {
            var active = current;
            return new ConnectionInfo(
                active.Client.AccountName,
                active.Client.Uri.Authority,
                active.IsCustom,
                active.IsLocal,
                active.ReadOnly,
                ReadOnlyLocked: readOnlyOption == true);
        }
    }

    public async Task UseAsync(BlobServiceClient client, bool allowWrites, CancellationToken cancellationToken)
    {
        // One page of one container is enough to prove the endpoint is reachable and the credentials are accepted.
        await foreach (var _ in client.GetBlobContainersAsync(cancellationToken: cancellationToken).AsPages(pageSizeHint: 1))
            break;

        var isLocal = LocalEndpoint.IsLocal(client.Uri);

        // A remote account typed in the page is writable only when the user asked for it; readOnly: true in the
        // AppHost wins over that.
        var readOnly = readOnlyOption == true || (!isLocal && !allowWrites);

        current = new Active(client, IsCustom: true, isLocal, readOnly);
    }

    public void Reset() => current = defaultConnection;
}
