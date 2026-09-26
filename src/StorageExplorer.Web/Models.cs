namespace StorageExplorer.Web;

internal sealed record ContainerSummary(string Name, DateTimeOffset? LastModified);

/// <param name="Name">Last path segment, e.g. <c>report.csv</c>.</param>
/// <param name="Path">Full blob name, or the folder prefix (ending in <c>/</c>) for folders.</param>
internal sealed record ExplorerEntry(
    string Name,
    string Path,
    bool IsFolder,
    long? Size,
    DateTimeOffset? LastModified,
    string? ContentType);

/// <param name="Truncated">True when the folder holds more entries than the listing limit.</param>
internal sealed record EntryListing(IReadOnlyList<ExplorerEntry> Entries, bool Truncated);

internal sealed record BlobDownload(Stream Content, string ContentType, string FileName);

/// <param name="ConnectionString">A storage account connection string, with the account key.</param>
/// <param name="AllowWrites">
/// Lets the explorer change data on a remote account, which is read-only otherwise. It has no effect on a local
/// connection, or when the AppHost locked the explorer with <c>readOnly: true</c>.
/// </param>
internal sealed record SetConnectionRequest(string? ConnectionString, bool AllowWrites = false);
