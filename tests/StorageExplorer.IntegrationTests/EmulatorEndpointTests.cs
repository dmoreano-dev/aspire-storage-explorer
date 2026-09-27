using StorageExplorer.Web.Blobs;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// The emulator reached through a name (<c>localhost</c>, <c>host.docker.internal</c>) and the random port Aspire gives
/// it, as when a connection string is pasted into "Change connection". For that kind of endpoint the SDK builds the
/// address of a blob without its container, and <c>BlobExplorerService</c> has to put it back (see its
/// <c>GetBlobClient</c>); before, download and delete failed for a blob that existed.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class EmulatorEndpointTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task ListContainersAsync_EmulatorReachedByName_ListsContainer()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        var service = ByName(container);

        // Act
        var actual = await service.ListContainersAsync(default);

        // Assert
        Assert.Contains(actual, c => c.Name == container.Name);
    }

    [Fact]
    public async Task ListEntriesAsync_EmulatorReachedByName_ListsFolderContent()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        await container.Upload("folder/a.txt", "content");
        var service = ByName(container);

        // Act
        var actual = await service.ListEntriesAsync(container.Name, "folder/", default);

        // Assert
        Assert.Equal(["folder/a.txt"], actual.Entries.Select(e => e.Path));
    }

    [Fact]
    public async Task DownloadAsync_EmulatorReachedByName_ReturnsBlob()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        await container.Upload("folder/a.txt", "content");
        var service = ByName(container);

        // Act
        var actual = await service.DownloadAsync(container.Name, "folder/a.txt", default);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("content", await ReadAll(actual.Content));
    }

    [Fact]
    public async Task DeleteAsync_EmulatorReachedByName_DeletesBlob()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        await container.Upload("folder/a.txt", "content");
        var service = ByName(container);

        // Act
        var actual = await service.DeleteAsync(container.Name, "folder/a.txt", default);

        // Assert
        Assert.True(actual);
        Assert.False(await container.Exists("folder/a.txt"));
    }

    [Fact]
    public async Task DownloadAsync_BlobPathStartingWithContainerName_ReturnsTheInnerBlob()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        await container.Upload("x.txt", "outer");
        await container.Upload($"{container.Name}/x.txt", "inner");
        var service = ByName(container);

        // Act
        var actual = await service.DownloadAsync(container.Name, $"{container.Name}/x.txt", default);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("inner", await ReadAll(actual.Content));
    }

    [Fact]
    public async Task DeleteAsync_BlobPathStartingWithContainerName_DeletesOnlyTheInnerBlob()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        await container.Upload("x.txt", "outer");
        await container.Upload($"{container.Name}/x.txt", "inner");
        var service = ByName(container);

        // Act
        var actual = await service.DeleteAsync(container.Name, $"{container.Name}/x.txt", default);

        // Assert
        Assert.True(actual);
        Assert.False(await container.Exists($"{container.Name}/x.txt"));
        Assert.True(await container.Exists("x.txt"));
    }

    [Fact]
    public async Task DownloadAsync_MissingBlobEmulatorReachedByName_ReturnsNull()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        var service = ByName(container);

        // Act
        var actual = await service.DownloadAsync(container.Name, "nope.txt", default);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task DeleteAsync_MissingBlobEmulatorReachedByName_ReturnsFalse()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        var service = ByName(container);

        // Act
        var actual = await service.DeleteAsync(container.Name, "nope.txt", default);

        // Assert
        Assert.False(actual);
    }

    // The service of the container, but reaching the emulator by its name.
    private BlobExplorerService ByName(TestContainer container) =>
        container.Service(connectionString: azurite.HostnameConnectionString);

    private static async Task<string> ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync();
    }
}
