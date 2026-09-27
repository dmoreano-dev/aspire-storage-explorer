namespace StorageExplorer.Web;

/// <param name="ConnectionString">A storage account connection string, with the account key.</param>
/// <param name="AllowWrites">
/// Lets the explorer change data on a remote account, which is read-only otherwise. It has no effect on a local
/// connection, or when the AppHost locked the explorer with <c>readOnly: true</c>.
/// </param>
internal sealed record SetConnectionRequest(string? ConnectionString, bool AllowWrites = false);
