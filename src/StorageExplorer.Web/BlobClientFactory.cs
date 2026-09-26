using System.Diagnostics.CodeAnalysis;
using Azure.Storage.Blobs;

namespace StorageExplorer.Web;

/// <summary>Creates the blob clients, so the connection string given at startup and the one typed in the UI behave the same.</summary>
internal static class BlobClientFactory
{
    // The default (5 retries with backoff) makes an unreachable endpoint take about 25 seconds to report.
    private static readonly BlobClientOptions Options = new() { Retry = { MaxRetries = 2 } };

    public static BlobServiceClient Create(string connectionString) => new(connectionString, Options);

    /// <returns><c>false</c> when the value is not a storage account connection string. Building a client only parses it; it never touches the network.</returns>
    public static bool TryCreate(string connectionString, [NotNullWhen(true)] out BlobServiceClient? client)
    {
        try
        {
            client = Create(connectionString);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            client = null;
            return false;
        }
    }
}
