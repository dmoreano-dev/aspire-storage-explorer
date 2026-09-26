using System.ComponentModel.DataAnnotations;

namespace StorageExplorer.Web;

internal sealed class StorageExplorerOptions
{
    public const string SectionName = "StorageExplorer";

    /// <summary>Connection string of the storage account to browse.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> locks the explorer to reading, whatever the connection. <c>false</c> makes the connection from the
    /// AppHost writable even when it is remote. Unset, only a local connection is writable, and a remote one typed in
    /// the page is writable only when the user allows it.
    /// </summary>
    public bool? ReadOnly { get; set; }
}
