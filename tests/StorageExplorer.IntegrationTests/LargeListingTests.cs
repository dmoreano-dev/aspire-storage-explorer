namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// The limits that keep a huge folder from making the page wait for ever. They sit at 5,000 entries, so the tests
/// need that many blobs.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class LargeListingTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task ListEntriesAsync_FolderWithExactlyMaxEntries_IsNotTruncated()
    {
        // Arrange
        var container = await Large();
        var service = container.Service();

        // Act
        var actual = await service.ListEntriesAsync(container.Name, "edge/", default);

        // Assert
        Assert.Equal(BlobExplorerService.MaxEntries, actual.Entries.Count);
        Assert.False(actual.Truncated);
    }

    [Fact]
    public async Task ListEntriesAsync_FolderOverMaxEntries_IsTruncated()
    {
        // Arrange
        var container = await Large();
        var service = container.Service();

        // Act
        var actual = await service.ListEntriesAsync(container.Name, "over/", default);

        // Assert
        Assert.Equal(BlobExplorerService.MaxEntries, actual.Entries.Count);
        Assert.True(actual.Truncated);
    }

    [Fact]
    public async Task SearchAsync_ExactlyMaxEntriesMatches_IsNotTruncated()
    {
        // Arrange
        var container = await Large();
        var service = container.Service();

        // Act
        var actual = await service.SearchAsync(container.Name, "edge/", ".txt", default);

        // Assert
        Assert.Equal(BlobExplorerService.MaxEntries, actual.Entries.Count);
        Assert.False(actual.Truncated);
    }

    [Fact]
    public async Task SearchAsync_OverMaxEntriesMatches_IsTruncated()
    {
        // Arrange
        var container = await Large();
        var service = container.Service();

        // Act
        var actual = await service.SearchAsync(container.Name, "over/", ".txt", default);

        // Assert
        Assert.Equal(BlobExplorerService.MaxEntries, actual.Entries.Count);
        Assert.True(actual.Truncated);
    }

    [Fact]
    public async Task SearchAsync_SingleMatchAmongManyBlobs_ReturnsMatchNotTruncated()
    {
        // Arrange
        // Over ten thousand blobs are read and one matches: the limit is on matches, not on what is read. It goes in
        // a folder of its own so the folders the other tests count stay as they are.
        var container = await Large();
        await container.Upload("needle/only-one.txt");
        var service = container.Service();

        // Act
        var actual = await service.SearchAsync(container.Name, null, "needle", default);

        // Assert
        var match = Assert.Single(actual.Entries);
        Assert.Equal("needle/only-one.txt", match.Path);
        Assert.False(actual.Truncated);
    }

    // Filled once and only read: about ten thousand uploads are too many to repeat for every test.
    private Task<TestContainer> Large() => azurite.SharedAsync("large", async () =>
    {
        var container = await TestContainer.CreateAsync(azurite);

        // Empty blobs, many at a time: what matters here is the count.
        await Fill(container, "edge/", BlobExplorerService.MaxEntries);
        await Fill(container, "over/", BlobExplorerService.MaxEntries + 1);

        return container;
    });

    private static Task Fill(TestContainer container, string folder, int count) =>
        Parallel.ForEachAsync(
            Enumerable.Range(0, count),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (index, _) => await container.Upload($"{folder}{index:D5}.txt", content: ""));
}
