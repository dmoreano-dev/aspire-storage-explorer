namespace Aspire.Hosting;

/// <summary>Coordinates of the explorer container image.</summary>
internal static class StorageExplorerContainerImageTags
{
    // The image is published to GitHub's registry, the release workflow checks it.
    // Before a release exists the image is resolved locally (build it with scripts/build-image.sh)
    // or can be overridden through the configureContainer callback.
    public static readonly string? Registry = "ghcr.io";
    public const string Image = "dmoreano-dev/storage-explorer";
    public const string Tag = "0.2.0";
}
