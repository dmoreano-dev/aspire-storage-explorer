using System.ComponentModel.DataAnnotations;

namespace StorageExplorer.Web;

internal sealed class StorageExplorerOptions
{
    public const string SectionName = "StorageExplorer";

    /// <summary>Connection string of the storage account to browse.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
