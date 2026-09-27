using System.Diagnostics.CodeAnalysis;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace StorageExplorer.Web;

/// <summary>
/// The blob, queue and table clients built from one connection string. <see cref="Blob"/> is always there: it is what
/// startup validates, and every connection needs it. <see cref="Queue"/> and <see cref="Table"/> are <c>null</c> when
/// the string does not carry enough to build them, for example a connection string scoped to a blob container SAS
/// (<c>BlobEndpoint</c> and <c>SharedAccessSignature</c>, no <c>AccountName</c>).
/// </summary>
internal sealed record StorageClients(BlobServiceClient Blob, QueueServiceClient? Queue, TableServiceClient? Table);

/// <summary>Creates the storage clients, so the connection string given at startup and the one typed in the UI behave the same.</summary>
internal static class StorageClientFactory
{
    // The default (5 retries with backoff) makes an unreachable endpoint take about 25 seconds to report.
    private static readonly BlobClientOptions BlobOptions = new() { Retry = { MaxRetries = 2 } };
    private static readonly QueueClientOptions QueueOptions = new() { Retry = { MaxRetries = 2 } };
    private static readonly TableClientOptions TableOptions = new() { Retry = { MaxRetries = 2 } };

    public static BlobServiceClient CreateBlob(string connectionString) => new(connectionString, BlobOptions);

    /// <returns><c>false</c> when the value is not a storage account connection string. Building a client only parses it; it never touches the network.</returns>
    public static bool TryCreateBlob(string connectionString, [NotNullWhen(true)] out BlobServiceClient? client)
    {
        try
        {
            client = CreateBlob(connectionString);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            client = null;
            return false;
        }
    }

    /// <summary>Builds the three clients from one connection string. Throws when even the blob client cannot be built.</summary>
    public static StorageClients Create(string connectionString) =>
        new(CreateBlob(connectionString), TryCreateQueue(connectionString), TryCreateTable(connectionString));

    /// <summary>
    /// Builds the three clients from one connection string. <c>false</c> when even the blob client cannot be built, the
    /// same case <see cref="TryCreateBlob"/> reports.
    /// </summary>
    public static bool TryCreate(string connectionString, [NotNullWhen(true)] out StorageClients? clients)
    {
        if (!TryCreateBlob(connectionString, out var blob))
        {
            clients = null;
            return false;
        }

        clients = new StorageClients(blob, TryCreateQueue(connectionString), TryCreateTable(connectionString));
        return true;
    }

    // Unlike CreateBlob, a connection string that cannot build a queue or table client is not an error: many valid
    // ones simply do not carry a queue or table endpoint (a blob container SAS, for example). The SDKs do not agree on
    // what they throw for that (FormatException, ArgumentException, InvalidOperationException, even a
    // NullReferenceException for a SAS scoped away from the service), so this catches broadly on purpose.

    private static QueueServiceClient? TryCreateQueue(string connectionString)
    {
        try
        {
            return new QueueServiceClient(connectionString, QueueOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TableServiceClient? TryCreateTable(string connectionString)
    {
        try
        {
            return new TableServiceClient(connectionString, TableOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
