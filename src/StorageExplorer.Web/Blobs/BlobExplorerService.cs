using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace StorageExplorer.Web.Blobs;

internal interface IBlobExplorerService
{
    Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken);

    Task<EntryListing> ListEntriesAsync(string container, string? prefix, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the blobs whose name, relative to <paramref name="prefix"/>, contains <paramref name="term"/> (not case
    /// sensitive), in that folder and all the folders below it. Azure can only filter by prefix, so this reads the
    /// listing and filters it here.
    /// </summary>
    Task<EntryListing> SearchAsync(string container, string? prefix, string term, CancellationToken cancellationToken);

    /// <returns>The blob content, or <c>null</c> when the blob does not exist.</returns>
    Task<BlobDownload?> DownloadAsync(string container, string path, CancellationToken cancellationToken);

    /// <summary>Creates the blob, or overwrites it when one with that name already exists.</summary>
    Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken);

    /// <returns><c>true</c> when the blob existed and was deleted.</returns>
    Task<bool> DeleteAsync(string container, string path, CancellationToken cancellationToken);

    /// <summary>Creates the container. Throws when one with that name already exists.</summary>
    Task CreateContainerAsync(string container, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an empty folder by uploading a hidden, zero-byte <c>.keep</c> placeholder blob inside it. Azure has no
    /// folder object of its own, so an otherwise-empty folder needs this to survive a refresh; <see
    /// cref="ListEntriesAsync"/> and <see cref="SearchAsync"/> never show that placeholder as a file. (A blob named
    /// exactly like the folder itself, ending in "/", was tried first, but Azurite does not keep a blob name's
    /// trailing "/": it comes back as a plain file called "path" instead of "path/", indistinguishable from real
    /// content.)
    /// </summary>
    Task CreateFolderAsync(string container, string path, CancellationToken cancellationToken);
}

internal sealed class BlobExplorerService(IStorageConnection connection) : IBlobExplorerService
{
    // The UI does not page yet, so cap the listing to keep very large folders responsive.
    internal const int MaxEntries = 5000;

    // A search reads blobs to find the ones that match, so it needs its own bound: this many blobs are read (about ten
    // requests to the account) before it gives up.
    internal const int MaxScannedBlobs = 50_000;

    private const string Delimiter = "/";

    // What CreateFolderAsync uploads to keep an otherwise-empty folder alive; see the interface doc comment for why
    // it is a normal file inside the folder rather than a blob named like the folder itself.
    private const string FolderPlaceholderName = ".keep";

    public async Task<IReadOnlyList<ContainerSummary>> ListContainersAsync(CancellationToken cancellationToken)
    {
        var containers = new List<ContainerSummary>();

        await foreach (var container in connection.Blob.GetBlobContainersAsync(cancellationToken: cancellationToken))
            containers.Add(new ContainerSummary(container.Name, container.Properties.LastModified));

        return containers;
    }

    public async Task<EntryListing> ListEntriesAsync(string container, string? prefix, CancellationToken cancellationToken)
    {
        var containerClient = connection.Blob.GetBlobContainerClient(container);
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

            // An empty folder's placeholder blob (see CreateFolderAsync); it carries no content of its own.
            if (IsFolderPlaceholder(item.Blob.Name))
                continue;

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

    public async Task<EntryListing> SearchAsync(string container, string? prefix, string term, CancellationToken cancellationToken)
    {
        var containerClient = connection.Blob.GetBlobContainerClient(container);
        var normalizedPrefix = NormalizePrefix(prefix);
        var prefixLength = normalizedPrefix?.Length ?? 0;
        var matches = new List<ExplorerEntry>();
        var scanned = 0;
        var truncated = false;

        // No delimiter: every blob under the prefix comes back, whatever its depth.
        await foreach (var blob in containerClient.GetBlobsAsync(
                           traits: BlobTraits.None,
                           states: BlobStates.None,
                           prefix: normalizedPrefix,
                           cancellationToken: cancellationToken))
        {
            if (scanned++ >= MaxScannedBlobs)
            {
                truncated = true;
                break;
            }

            if (IsFolderPlaceholder(blob.Name))
                continue;

            var relativeName = blob.Name[prefixLength..];
            if (!relativeName.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;

            if (matches.Count >= MaxEntries)
            {
                truncated = true;
                break;
            }

            var properties = blob.Properties;
            matches.Add(new ExplorerEntry(
                relativeName,
                blob.Name,
                false,
                properties.ContentLength,
                properties.LastModified,
                properties.ContentType));
        }

        return new EntryListing(matches, truncated);
    }

    public async Task<BlobDownload?> DownloadAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(connection.Blob, container, path);

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

    public async Task UploadAsync(string container, string path, Stream content, string? contentType, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(connection.Blob, container, path);

        // No conditions set on the options means this overwrites a blob already at that name, the same way dropping a
        // file with the same name onto a folder replaces it on a desktop file explorer.
        var options = new BlobUploadOptions();
        if (!string.IsNullOrEmpty(contentType))
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };

        await blobClient.UploadAsync(content, options, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(connection.Blob, container, path);
        var response = await blobClient.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);

        return response.Value;
    }

    public async Task CreateContainerAsync(string container, CancellationToken cancellationToken) =>
        await connection.Blob.GetBlobContainerClient(container).CreateAsync(cancellationToken: cancellationToken);

    public async Task CreateFolderAsync(string container, string path, CancellationToken cancellationToken)
    {
        var blobClient = GetBlobClient(connection.Blob, container, NormalizePrefix(path) + FolderPlaceholderName);

        await blobClient.UploadAsync(Stream.Null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The client of a blob. <c>GetBlobContainerClient(container).GetBlobClient(path)</c> is not enough on its own: the
    /// SDK reads an emulator address (<c>host/account/container/blob</c>) as such only when the host is an IPv4 address or
    /// the port is one of the emulator's (10000 to 10002). With anything else, for example <c>localhost</c> and the random
    /// port Aspire gives Azurite, it takes the account for the container, and the address it builds for the blob has no
    /// container in it: the blob is looked for in the wrong place, and download and delete fail for a blob that exists
    /// (not found when it is in a folder, and bad request when it is at the root, as its name is taken for a container).
    /// </summary>
    /// <remarks>
    /// That happens because the SDK builds the address by replacing what it took for the blob name, so giving it the
    /// container and the path together puts the container back. This is done only when the first address is not the
    /// container followed by the path, and only kept when the second one is. Otherwise, the SDK's own answer is used,
    /// as before.
    /// </remarks>
    internal static BlobClient GetBlobClient(BlobServiceClient service, string container, string path)
    {
        var containerClient = service.GetBlobContainerClient(container);
        var blob = containerClient.GetBlobClient(path);

        if (IsBlobOf(containerClient, blob, path))
            return blob;

        var withContainer = containerClient.GetBlobClient($"{container}{Delimiter}{path}");

        return IsBlobOf(containerClient, withContainer, path) ? withContainer : blob;
    }

    // The whole path has to match and not only its start: a blob called "photos/x" in the container "photos" would pass
    // for the blob "x" that the container "photos" holds.
    private static bool IsBlobOf(BlobContainerClient container, BlobClient blob, string path) =>
        Uri.UnescapeDataString(blob.Uri.AbsolutePath) == Uri.UnescapeDataString(container.Uri.AbsolutePath) + Delimiter + path;

    private static string? NormalizePrefix(string? prefix) =>
        string.IsNullOrEmpty(prefix) || prefix.EndsWith(Delimiter, StringComparison.Ordinal)
            ? prefix
            : prefix + Delimiter;

    private static bool IsFolderPlaceholder(string blobName) =>
        blobName.EndsWith(Delimiter + FolderPlaceholderName, StringComparison.Ordinal);

    private static string LastSegment(string path) =>
        path.TrimEnd('/').Split('/')[^1];
}
