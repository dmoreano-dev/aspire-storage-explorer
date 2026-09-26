using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace StorageExplorer.Web;

internal interface IBlobExplorerService
{
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken);

    Task<EntryListing> ListEntriesAsync(string container, string? prefix, CancellationToken cancellationToken);

    /// <returns>The blob content, or <c>null</c> when the blob does not exist.</returns>
    Task<BlobDownload?> DownloadAsync(string container, string path, CancellationToken cancellationToken);

    /// <returns><c>true</c> when the blob existed and was deleted.</returns>
    Task<bool> DeleteAsync(string container, string path, CancellationToken cancellationToken);
}

internal sealed class BlobExplorerService(IStorageConnection connection) : IBlobExplorerService
{
    // The UI does not page yet, so cap the listing to keep very large folders responsive.
    internal const int MaxEntries = 5000;

    private const string Delimiter = "/";

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var containers = new List<ContainerSummary>();

        await foreach (var container in connection.Client.GetBlobContainersAsync(cancellationToken: cancellationToken))
            containers.Add(new ContainerSummary(container.Name, container.Properties.LastModified));

        return containers;
    }

    public async Task<EntryListing> ListEntriesAsync(string container, string? prefix, CancellationToken cancellationToken)
    {
        var containerClient = connection.Client.GetBlobContainerClient(container);
        var folders = new List<ExplorerEntry>();
        var files = new List<ExplorerEntry>();
        var truncated = false;

        await foreach (var item in containerClient.GetBlobsByHierarchyAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           delimiter: Delimiter,
                           prefix: NormalizePrefix(prefix),
                           cancellationToken: cancellationToken))
        {
            if (folders.Count + files.Count >= MaxEntries)
            {
                truncated = true;
                break;
            }

            if (item.IsPrefix)
            {
                folders.Add(new ExplorerEntry(LastSegment(item.Prefix), item.Prefix, true, null, null, null));
                continue;
            }

            var properties = item.Blob.Properties;
            files.Add(new ExplorerEntry(
                LastSegment(item.Blob.Name),
                item.Blob.Name,
                false,
                properties.ContentLength,
                properties.LastModified,
                properties.ContentType));
        }

        return new EntryListing([.. folders, .. files], truncated);
    }

    public async Task<BlobDownload?> DownloadAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blobClient = connection.Client.GetBlobContainerClient(container).GetBlobClient(path);

        try
        {
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var contentType = response.Value.Details.ContentType;

            return new BlobDownload(
                response.Value.Content,
                string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                LastSegment(path));
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blobClient = connection.Client.GetBlobContainerClient(container).GetBlobClient(path);
        var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);

        return response.Value;
    }

    private static string? NormalizePrefix(string? prefix) =>
        string.IsNullOrEmpty(prefix) || prefix.EndsWith(Delimiter, StringComparison.Ordinal)
            ? prefix
            : prefix + Delimiter;

    private static string LastSegment(string path) =>
        path.TrimEnd('/').Split('/')[^1];
}
