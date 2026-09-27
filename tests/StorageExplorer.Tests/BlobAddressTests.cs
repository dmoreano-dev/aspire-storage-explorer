using Azure.Storage.Blobs;
using StorageExplorer.Web.Blobs;

namespace StorageExplorer.Web.Tests;

/// <summary>
/// The address of a blob for every kind of endpoint. It only computes addresses, so it needs no network. The bug
/// these guard against: for some endpoints the SDK builds the address of a blob without its container, and download
/// and delete then look for the blob in the wrong place.
/// </summary>
public class BlobAddressTests
{
    private const string Container = "photos";

    // The SDK gets some of these right, and the others are the ones it got wrong.
    private static readonly string[] Endpoints =
    [
        "127.0.0.1:10000",
        "127.0.0.1:33113",
        "10.0.0.5:33113",
        "localhost:10000",
        "localhost:10001",
        "storage:10000",
        "localhost:33113",
        "storage:33113",
        "host.docker.internal:33113",
        "[::1]:33113",
        "[::1]:10000",
        "localhost",
        "storage",
    ];

    private static readonly string[] Paths =
    [
        "cat.png",
        "2025/cat.png",
        "2025/q1/deep/cat.png",
        "a b/ñandú (1).txt",
        "100%.txt",
        "a+b&c=d.txt",
        "question?.txt",
        "hash#tag.txt",
        "emoji-😀/f.txt",
        "%41.txt",
        // Names that start like the container: the address of "photos/x" in "photos" must not pass for the one of "x".
        "photos",
        "photos/x",
        "photos/photos/x",
    ];

    public static IEnumerable<object[]> EmulatorEndpoints => Endpoints.Select(endpoint => new object[] { endpoint });

    public static IEnumerable<object[]> BlobPaths => Paths.Select(path => new object[] { path });

    public static IEnumerable<object[]> EmulatorEndpointsAndBlobPaths =>
        Endpoints.SelectMany(endpoint => Paths.Select(path => new object[] { endpoint, path }));

    [Theory]
    [MemberData(nameof(EmulatorEndpointsAndBlobPaths))]
    public void GetBlobClient_EmulatorEndpoint_AddressesBlobBelowAccountAndContainer(string hostAndPort, string path)
    {
        // Arrange
        var service = StorageClientFactory.CreateBlob(Emulator(hostAndPort));

        // Act
        var actual = BlobExplorerService.GetBlobClient(service, Container, path);

        // Assert
        Assert.Equal($"/devstoreaccount1/{Container}/{path}", PathOf(actual));
        Assert.Equal(service.Uri.Authority, actual.Uri.Authority);
        Assert.Equal("", actual.Uri.Query);
    }

    [Theory]
    [MemberData(nameof(BlobPaths))]
    public void GetBlobClient_AccountWithOwnHost_AddressesBlobBelowContainer(string path)
    {
        // Arrange
        var service = StorageClientFactory.CreateBlob(Azure);

        // Act
        var actual = BlobExplorerService.GetBlobClient(service, Container, path);

        // Assert
        Assert.Equal($"/{Container}/{path}", PathOf(actual));
        Assert.Equal("acct.blob.example.invalid", actual.Uri.Authority);
    }

    [Theory]
    [MemberData(nameof(EmulatorEndpoints))]
    public void GetBlobClient_EmulatorEndpoint_KeepsConnectionCredentials(string hostAndPort)
    {
        // Arrange
        var service = StorageClientFactory.CreateBlob(Emulator(hostAndPort));
        var container = service.GetBlobContainerClient(Container);

        // Act
        var actual = BlobExplorerService.GetBlobClient(service, Container, "2025/cat.png");

        // Assert
        // Only a client built with the account key can sign, so this says the key was not lost on the way.
        Assert.True(container.CanGenerateSasUri);
        Assert.True(actual.CanGenerateSasUri);
        Assert.Equal(container.AccountName, actual.AccountName);
    }

    [Theory]
    [InlineData(@"a\b.txt")]
    [InlineData("a/../b.txt")]
    [InlineData("a//b.txt")]
    public void GetBlobClient_NameTheAddressCannotCarry_FallsBackToSdkAnswer(string path)
    {
        // Arrange
        var service = StorageClientFactory.CreateBlob(Emulator("localhost:33113"));

        // Act
        var actual = BlobExplorerService.GetBlobClient(service, Container, path);

        // Assert
        Assert.NotNull(actual);
    }

    private static string Emulator(string hostAndPort) =>
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={TestConnectionStrings.EmulatorKey};" +
        $"BlobEndpoint=http://{hostAndPort}/devstoreaccount1;";

    private static string Azure =>
        $"DefaultEndpointsProtocol=https;AccountName=acct;AccountKey={TestConnectionStrings.EmulatorKey};" +
        "BlobEndpoint=https://acct.blob.example.invalid;";

    private static string PathOf(BlobClient blob) => Uri.UnescapeDataString(blob.Uri.AbsolutePath);
}
