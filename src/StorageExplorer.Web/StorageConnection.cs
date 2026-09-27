using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web;

/// <summary>What the UI may know about the active connection. It never carries the account key.</summary>
/// <param name="AccountName">The storage account name.</param>
/// <param name="Endpoint">Host and port of the blob endpoint, without path or query.</param>
/// <param name="IsCustom"><c>true</c> when it was set from the UI instead of the AppHost.</param>
/// <param name="IsLocal"><c>true</c> when the endpoint is on this machine, <c>false</c> for a remote account.</param>
/// <param name="ReadOnly"><c>true</c> when the explorer refuses to change data on this connection.</param>
/// <param name="ReadOnlyLocked"><c>true</c> when the AppHost set <c>readOnly: true</c>, so the page cannot allow writes.</param>
/// <param name="HasQueues">
/// <c>true</c> when the connection string carries enough to reach the account's queues. For the connection the AppHost
/// gives, this only checks that a queue client could be built (a blob-scoped SAS connection string cannot); for one
/// typed in the page, listing queues was tried and it worked, which also catches an account kind that has no queues.
/// </param>
/// <param name="HasTables">The same as <see cref="HasQueues"/>, for tables.</param>
internal sealed record ConnectionInfo(
    string AccountName,
    string Endpoint,
    bool IsCustom,
    bool IsLocal,
    bool ReadOnly,
    bool ReadOnlyLocked,
    bool HasQueues,
    bool HasTables);

/// <summary>The storage account the explorer is browsing. It can be swapped at runtime.</summary>
internal interface IStorageConnection
{
    BlobServiceClient Blob { get; }

    /// <summary><c>null</c> when the connection string cannot reach the account's queues; see <see cref="ConnectionInfo.HasQueues"/>.</summary>
    QueueServiceClient? Queue { get; }

    /// <summary><c>null</c> when the connection string cannot reach the account's tables; see <see cref="ConnectionInfo.HasTables"/>.</summary>
    TableServiceClient? Table { get; }

    ConnectionInfo Info { get; }

    /// <summary>
    /// Checks that the clients can reach the account, then makes them the active connection. Listing the containers is
    /// mandatory and failing it throws, leaving the active connection as it was; listing queues and tables (when their
    /// client could be built at all) only decides <see cref="ConnectionInfo.HasQueues"/> and
    /// <see cref="ConnectionInfo.HasTables"/>, since an account may simply not have one of those services.
    /// </summary>
    /// <param name="clients">The clients of the new connection.</param>
    /// <param name="allowWrites">Lets the explorer change data when the connection is remote.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UseAsync(StorageClients clients, bool allowWrites, CancellationToken cancellationToken);

    /// <summary>Goes back to the connection string the explorer was started with.</summary>
    void Reset();
}

/// <summary>
/// Keeps the active connection in memory only: a connection set from the UI is lost when the explorer restarts.
/// </summary>
internal sealed class StorageConnection : IStorageConnection
{
    private sealed record Active(StorageClients Clients, bool IsCustom, bool IsLocal, bool ReadOnly, bool HasQueues, bool HasTables);

    private readonly bool? readOnlyOption;
    private readonly Active defaultConnection;
    private volatile Active current;

    public StorageConnection(IOptions<StorageExplorerOptions> options)
    {
        readOnlyOption = options.Value.ReadOnly;

        var clients = StorageClientFactory.Create(options.Value.ConnectionString);
        var isLocal = LocalEndpoint.IsLocal(clients.Blob.Uri);

        // The AppHost decides for its own connection; without a choice only a local one is writable. Queues and
        // tables are not probed here (the constructor never touches the network); a client that could be built at
        // all is optimistically assumed to work, and Change connection is what actually checks it.
        defaultConnection = new Active(
            clients,
            IsCustom: false,
            isLocal,
            ReadOnly: readOnlyOption ?? !isLocal,
            HasQueues: clients.Queue is not null,
            HasTables: clients.Table is not null);
        current = defaultConnection;
    }

    public BlobServiceClient Blob => current.Clients.Blob;

    public QueueServiceClient? Queue => current.Clients.Queue;

    public TableServiceClient? Table => current.Clients.Table;

    public ConnectionInfo Info
    {
        get
        {
            var active = current;
            return new ConnectionInfo(
                active.Clients.Blob.AccountName,
                active.Clients.Blob.Uri.Authority,
                active.IsCustom,
                active.IsLocal,
                active.ReadOnly,
                ReadOnlyLocked: readOnlyOption == true,
                active.HasQueues,
                active.HasTables);
        }
    }

    public async Task UseAsync(StorageClients clients, bool allowWrites, CancellationToken cancellationToken)
    {
        // One page of one container is enough to prove the endpoint is reachable and the credentials are accepted.
        await foreach (var _ in clients.Blob.GetBlobContainersAsync(cancellationToken: cancellationToken).AsPages(pageSizeHint: 1))
            break;

        var hasQueues = await ProbeAsync(clients.Queue, service => service.GetQueuesAsync(cancellationToken: cancellationToken), cancellationToken);
        var hasTables = await ProbeAsync(clients.Table, service => service.QueryAsync(maxPerPage: 1, cancellationToken: cancellationToken), cancellationToken);

        var isLocal = LocalEndpoint.IsLocal(clients.Blob.Uri);

        // A remote account typed in the page is writable only when the user asked for it; readOnly: true in the
        // AppHost wins over that.
        var readOnly = readOnlyOption == true || (!isLocal && !allowWrites);

        current = new Active(clients, IsCustom: true, isLocal, readOnly, hasQueues, hasTables);
    }

    public void Reset() => current = defaultConnection;

    /// <summary>
    /// Tries to read the first page of a queue or table listing. A failure only means the account does not offer that
    /// service (or the connection string could not reach it in the first place, when <paramref name="client"/> is
    /// already <c>null</c>); it never fails the connection switch.
    /// </summary>
    private static async Task<bool> ProbeAsync<TClient, TItem>(
        TClient? client,
        Func<TClient, AsyncPageable<TItem>> list,
        CancellationToken cancellationToken)
        where TClient : class
        where TItem : notnull
    {
        if (client is null)
            return false;

        try
        {
            await foreach (var _ in list(client).AsPages(pageSizeHint: 1).WithCancellation(cancellationToken))
                break;

            return true;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested is false)
        {
            return false;
        }
    }
}
