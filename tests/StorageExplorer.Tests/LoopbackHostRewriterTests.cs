namespace StorageExplorer.Web.Tests;

public class LoopbackHostRewriterTests
{
    private const string Key = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public void Rewrite_LoopbackHost_ReturnsHostMachine(string loopback)
    {
        // Arrange
        var connectionString = Emulator(loopback, loopback, loopback);
        var expected = Emulator("host.docker.internal", "host.docker.internal", "host.docker.internal");

        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rewrite_LoopbackHostWithoutPort_ReplacesHost()
    {
        // Arrange
        const string connectionString = "BlobEndpoint=http://localhost/devstoreaccount1;AccountName=a;AccountKey=k";
        const string expected = "BlobEndpoint=http://host.docker.internal/devstoreaccount1;AccountName=a;AccountKey=k";

        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rewrite_DevInternalHost_ReturnsPlainContainerName()
    {
        // Arrange
        var connectionString = Emulator("storage.dev.internal", "storage.dev.internal", "storage.dev.internal");
        var expected = Emulator("storage", "storage", "storage");

        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rewrite_DevInternalHostInAnyCase_ReturnsPlainContainerName()
    {
        // Arrange
        var connectionString = Emulator("Storage.DEV.Internal");
        var expected = Emulator("Storage");

        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rewrite_DevelopmentStorageShortcut_ExpandsToEmulatorOnHostMachine()
    {
        // Arrange
        const string shortcut = "UseDevelopmentStorage=true";

        // Act
        var actual = LoopbackHostRewriter.Rewrite(shortcut);

        // Assert
        Assert.Contains("AccountName=devstoreaccount1", actual);
        Assert.Contains("BlobEndpoint=http://host.docker.internal:10000/devstoreaccount1", actual);
        Assert.Contains("QueueEndpoint=http://host.docker.internal:10001/devstoreaccount1", actual);
        Assert.Contains("TableEndpoint=http://host.docker.internal:10002/devstoreaccount1", actual);
    }

    [Fact]
    public void TryCreate_RewrittenDevelopmentStorageShortcut_ReturnsTrue()
    {
        // Arrange
        var rewritten = LoopbackHostRewriter.Rewrite("UseDevelopmentStorage=true");

        // Act
        var actual = BlobClientFactory.TryCreate(rewritten, out _);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("UseDevelopmentStorage=TRUE")]
    [InlineData(" UseDevelopmentStorage = true ")]
    [InlineData("UseDevelopmentStorage=true;")]
    public void Rewrite_DevelopmentStorageShortcutWithCaseAndSpacing_ExpandsToEmulator(string shortcut)
    {
        // Act
        var actual = LoopbackHostRewriter.Rewrite(shortcut);

        // Assert
        Assert.Contains("host.docker.internal:10000", actual);
    }

    [Theory]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=acct;AccountKey=k;EndpointSuffix=core.windows.net")]
    [InlineData("BlobEndpoint=https://acct.blob.core.windows.net;SharedAccessSignature=sv=2024")]
    [InlineData("BlobEndpoint=http://10.0.0.5:10000/acct;AccountName=acct;AccountKey=k")]
    // Only the host of an endpoint URI counts: the same words in another place must not be touched.
    [InlineData("AccountName=localhost;AccountKey=k;BlobEndpoint=https://acct.blob.core.windows.net")]
    // A host that only starts like a loopback one is another host.
    [InlineData("BlobEndpoint=http://localhost.evil.com:10000/acct;AccountName=a;AccountKey=k")]
    [InlineData("BlobEndpoint=http://127.0.0.1.evil.com:10000/acct;AccountName=a;AccountKey=k")]
    [InlineData("BlobEndpoint=http://storage.dev.internal.evil.com:10000/acct;AccountName=a;AccountKey=k")]
    public void Rewrite_OtherHosts_ReturnsSameConnectionString(string connectionString)
    {
        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Equal(connectionString, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Rewrite_EmptyValue_ReturnsSameValue(string? connectionString)
    {
        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString!);

        // Assert
        Assert.Equal(connectionString, actual);
    }

    [Fact]
    public void Rewrite_LoopbackHost_KeepsAccountKey()
    {
        // Arrange
        var connectionString = Emulator("127.0.0.1");

        // Act
        var actual = LoopbackHostRewriter.Rewrite(connectionString);

        // Assert
        Assert.Contains($"AccountKey={Key};", actual);
    }

    private static string Emulator(string blobHost, string? queueHost = null, string? tableHost = null) =>
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={Key};" +
        $"BlobEndpoint=http://{blobHost}:10000/devstoreaccount1;" +
        (queueHost is null ? "" : $"QueueEndpoint=http://{queueHost}:10001/devstoreaccount1;") +
        (tableHost is null ? "" : $"TableEndpoint=http://{tableHost}:10002/devstoreaccount1;");
}
