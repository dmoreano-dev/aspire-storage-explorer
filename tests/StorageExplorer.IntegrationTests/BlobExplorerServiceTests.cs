using Azure;
using Azure.Storage.Blobs.Models;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>The service against a real Azurite: what the emulator does is what the tests believe.</summary>
[Collection(AzuriteCollection.Name)]
public sealed class BlobExplorerServiceTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestContainer container = null!;
    private BlobExplorerService service = null!;

    public async Task InitializeAsync()
    {
        container = await TestContainer.CreateAsync(azurite);
        service = container.Service();

        await Task.WhenAll(
            container.Upload("readme.txt", "hello", "text/plain"),
            container.Upload("data.json", "{}", "application/json"),
            container.Upload("Reports FINAL.txt", "final", "text/plain"),
            container.Upload("2025/notes.txt", "notes", "text/plain"),
            container.Upload("2025/q1/report.csv", "a,b\n1,2\n", "text/csv"),
            container.Upload("2025/q1/summary.txt", "summary", "text/plain"),
            container.Upload("2025/q2/report.csv", "a,b\n3,4\n", "text/csv"),
            container.Upload("2026/plan.md", "# plan", "text/markdown"));
    }

    public async Task DisposeAsync() => await container.DisposeAsync();

    // ---- containers ----

    [Fact]
    public async Task ListContainersAsync_ExistingContainer_ReturnsItWithLastModifiedDate()
    {
        // Act
        var actual = await service.ListContainersAsync(default);

        // Assert
        var listed = Assert.Single(actual, c => c.Name == container.Name);
        Assert.NotNull(listed.LastModified);
    }

    // ---- entries ----

    [Fact]
    public async Task ListEntriesAsync_Root_ReturnsFoldersBeforeFiles()
    {
        // Act
        var actual = await service.ListEntriesAsync(container.Name, null, default);

        // Assert
        Assert.False(actual.Truncated);
        Assert.Equal(["2025", "2026", "Reports FINAL.txt", "data.json", "readme.txt"], actual.Entries.Select(e => e.Name));
        Assert.Equal([true, true, false, false, false], actual.Entries.Select(e => e.IsFolder));
    }

    [Fact]
    public async Task ListEntriesAsync_Folder_HasPrefixAsPathAndNoSizeOrType()
    {
        // Act
        var listing = await service.ListEntriesAsync(container.Name, null, default);

        // Assert
        var folder = listing.Entries.First(e => e.Name == "2025");
        Assert.Equal("2025/", folder.Path);
        Assert.True(folder.IsFolder);
        Assert.Null(folder.Size);
        Assert.Null(folder.LastModified);
        Assert.Null(folder.ContentType);
    }

    [Fact]
    public async Task ListEntriesAsync_File_HasSizeTypeAndDate()
    {
        // Act
        var listing = await service.ListEntriesAsync(container.Name, null, default);

        // Assert
        var file = listing.Entries.First(e => e.Name == "data.json");
        Assert.Equal("data.json", file.Path);
        Assert.False(file.IsFolder);
        Assert.Equal(2, file.Size);
        Assert.Equal("application/json", file.ContentType);
        Assert.NotNull(file.LastModified);
    }

    [Theory]
    [InlineData("2025/")]
    [InlineData("2025")]
    public async Task ListEntriesAsync_FolderPrefixWithOrWithoutSlash_ListsDirectChildren(string prefix)
    {
        // Act
        var actual = await service.ListEntriesAsync(container.Name, prefix, default);

        // Assert
        Assert.Equal(["q1", "q2", "notes.txt"], actual.Entries.Select(e => e.Name));
        Assert.Equal(["2025/q1/", "2025/q2/", "2025/notes.txt"], actual.Entries.Select(e => e.Path));
    }

    [Fact]
    public async Task ListEntriesAsync_PrefixSharingStartWithAnotherFolder_DoesNotMixThemUp()
    {
        // Arrange
        await container.Upload("20250/other.txt");

        // Act
        var actual = await service.ListEntriesAsync(container.Name, "2025", default);

        // Assert
        Assert.DoesNotContain(actual.Entries, e => e.Path.StartsWith("20250", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListEntriesAsync_MissingFolder_ReturnsEmptyListing()
    {
        // Act
        var actual = await service.ListEntriesAsync(container.Name, "nope/", default);

        // Assert
        Assert.Empty(actual.Entries);
        Assert.False(actual.Truncated);
    }

    [Fact]
    public async Task ListEntriesAsync_MissingContainer_ThrowsContainerNotFound()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.ListEntriesAsync("no-such-container", null, default));

        // Assert
        Assert.Equal(404, actual.Status);
        Assert.Equal("ContainerNotFound", actual.ErrorCode);
    }

    // ---- search ----

    [Fact]
    public async Task SearchAsync_TextInAnyCase_FindsBlobsInEveryFolderBelow()
    {
        // Act
        var actual = await service.SearchAsync(container.Name, null, "REPORT", default);

        // Assert
        Assert.False(actual.Truncated);
        Assert.Equal(
            ["2025/q1/report.csv", "2025/q2/report.csv", "Reports FINAL.txt"],
            actual.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.All(actual.Entries, entry => Assert.False(entry.IsFolder));
    }

    [Fact]
    public async Task SearchAsync_FolderPrefix_NamesResultsByPathBelowFolder()
    {
        // Act
        var actual = await service.SearchAsync(container.Name, "2025/", "report", default);

        // Assert
        Assert.Equal(["q1/report.csv", "q2/report.csv"], actual.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["2025/q1/report.csv", "2025/q2/report.csv"], actual.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_TextOnlyInFolderPrefix_ReturnsNoMatches()
    {
        // Act
        var actual = await service.SearchAsync(container.Name, "2025/", "2025", default);

        // Assert
        Assert.Empty(actual.Entries);
    }

    [Fact]
    public async Task SearchAsync_TextWithFolderNameBelow_MatchesFolderNamesInPath()
    {
        // Act
        var actual = await service.SearchAsync(container.Name, "2025/", "q1/", default);

        // Assert
        Assert.Equal(["q1/report.csv", "q1/summary.txt"], actual.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("2025/")]
    [InlineData("2025")]
    public async Task SearchAsync_FolderPrefixWithOrWithoutSlash_ReturnsMatchesBelowFolder(string prefix)
    {
        // Act
        var actual = await service.SearchAsync(container.Name, prefix, "report", default);

        // Assert
        Assert.Equal(["2025/q1/report.csv", "2025/q2/report.csv"], actual.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(".*")]
    [InlineData("*")]
    [InlineData("%")]
    [InlineData("[a-z]")]
    [InlineData("nothing like this")]
    public async Task SearchAsync_TermWithoutLiteralMatch_ReturnsEmptyListing(string term)
    {
        // Act
        var actual = await service.SearchAsync(container.Name, null, term, default);

        // Assert
        Assert.Empty(actual.Entries);
        Assert.False(actual.Truncated);
    }

    [Fact]
    public async Task SearchAsync_MissingContainer_ThrowsNotFound()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.SearchAsync("no-such-container", null, "x", default));

        // Assert
        Assert.Equal(404, actual.Status);
    }

    // ---- download ----

    [Fact]
    public async Task DownloadAsync_ExistingBlob_ReturnsContentTypeAndFileName()
    {
        // Act
        var actual = await service.DownloadAsync(container.Name, "2025/q1/report.csv", default);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal("text/csv", actual.ContentType);
        Assert.Equal("report.csv", actual.FileName);
        Assert.Equal("a,b\n1,2\n", await ReadAll(actual.Content));
    }

    [Fact]
    public async Task DownloadAsync_MissingBlob_ReturnsNull()
    {
        // Act
        var actual = await service.DownloadAsync(container.Name, "nope.txt", default);

        // Assert
        Assert.Null(actual);
    }

    [Fact]
    public async Task DownloadAsync_MissingContainer_ReturnsNull()
    {
        // Act
        var actual = await service.DownloadAsync("no-such-container", "nope.txt", default);

        // Assert
        Assert.Null(actual);
    }

    // ---- delete ----

    [Fact]
    public async Task DeleteAsync_ExistingBlob_DeletesOnlyThatBlob()
    {
        // Act
        var actual = await service.DeleteAsync(container.Name, "2025/q1/report.csv", default);

        // Assert
        Assert.True(actual);
        Assert.False(await container.Exists("2025/q1/report.csv"));
        Assert.True(await container.Exists("2025/q2/report.csv"));
        Assert.True(await container.Exists("2025/q1/summary.txt"));
    }

    [Fact]
    public async Task DeleteAsync_MissingBlob_ReturnsFalse()
    {
        // Act
        var actual = await service.DeleteAsync(container.Name, "nope.txt", default);

        // Assert
        Assert.False(actual);
    }

    [Fact]
    public async Task DeleteAsync_BlobWithSnapshot_DeletesSnapshotsToo()
    {
        // Arrange
        await container.Client.GetBlobClient("readme.txt").CreateSnapshotAsync();

        // Act
        var actual = await service.DeleteAsync(container.Name, "readme.txt", default);

        // Assert
        Assert.True(actual);
        var left = new List<string>();
        await foreach (var blob in container.Client.GetBlobsAsync(BlobTraits.None, BlobStates.Snapshots, "readme.txt", default))
            left.Add(blob.Name);
        Assert.Empty(left);
    }

    [Fact]
    public async Task DeleteAsync_FolderPrefix_ReturnsFalseAndDeletesNothing()
    {
        // Act
        var actual = await service.DeleteAsync(container.Name, "2025/", default);

        // Assert
        Assert.False(actual);
        Assert.True(await container.Exists("2025/notes.txt"));
    }

    // ---- names that need escaping ----

    // The full name of the blob, the folder it is in (or null for the root of the container) and its own name.
    public static TheoryData<string, string?, string> SpecialNames => new()
    {
        { "ñandú/informe final (1).txt", "ñandú/", "informe final (1).txt" },
        { "a+b&c=d.txt", null, "a+b&c=d.txt" },
        { "100%.txt", null, "100%.txt" },
        { "hash#tag.txt", null, "hash#tag.txt" },
        { "question?.txt", null, "question?.txt" },
        { "dir with space/file name.txt", "dir with space/", "file name.txt" },
        { "emoji-😀/file.txt", "emoji-😀/", "file.txt" },
    };

    [Theory]
    [MemberData(nameof(SpecialNames))]
    public async Task ListEntriesAsync_SpecialCharactersInName_ListsBlob(string name, string? folder, string leaf)
    {
        // Arrange
        await container.Upload(name, content: name);

        // Act
        var actual = await service.ListEntriesAsync(container.Name, folder, default);

        // Assert
        Assert.Contains(actual.Entries, e => e.Path == name && e.Name == leaf);
    }

    [Theory]
    [MemberData(nameof(SpecialNames))]
    public async Task SearchAsync_SpecialCharactersInName_FindsBlob(string name, string? folder, string leaf)
    {
        // Arrange
        await container.Upload(name, content: name);

        // Act
        var actual = await service.SearchAsync(container.Name, null, leaf, default);

        // Assert
        Assert.Contains(actual.Entries, e => e.Path == name);
    }

    [Theory]
    [MemberData(nameof(SpecialNames))]
    public async Task DownloadAsync_SpecialCharactersInName_ReturnsContentAndFileName(string name, string? folder, string leaf)
    {
        // Arrange
        await container.Upload(name, content: name);

        // Act
        var actual = await service.DownloadAsync(container.Name, name, default);

        // Assert
        Assert.NotNull(actual);
        Assert.Equal(leaf, actual.FileName);
        Assert.Equal(name, await ReadAll(actual.Content));
    }

    [Theory]
    [MemberData(nameof(SpecialNames))]
    public async Task DeleteAsync_SpecialCharactersInName_DeletesBlob(string name, string? folder, string leaf)
    {
        // Arrange
        await container.Upload(name, content: name);

        // Act
        var actual = await service.DeleteAsync(container.Name, name, default);

        // Assert
        Assert.True(actual);
        Assert.False(await container.Exists(name));
    }

    private static async Task<string> ReadAll(Stream stream)
    {
        await using (stream)
        using (var reader = new StreamReader(stream))
            return await reader.ReadToEndAsync();
    }
}
