using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web.Tests;

public class StorageConnectionTests
{
    // ---- the connection the AppHost gives ----

    [Fact]
    public void Info_LocalAccountFromAppHost_IsWritable()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.Equal("devstoreaccount1", actual.AccountName);
        Assert.True(actual.IsLocal);
        Assert.False(actual.IsCustom);
        Assert.False(actual.ReadOnly);
        Assert.False(actual.ReadOnlyLocked);
    }

    [Fact]
    public void Info_RemoteAccountFromAppHost_IsReadOnlyByDefault()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Remote);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.False(actual.IsLocal);
        Assert.True(actual.ReadOnly);
        Assert.False(actual.ReadOnlyLocked);
    }

    [Fact]
    public void Info_ReadOnlyTrueOnLocalAccount_IsLocked()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local, readOnly: true);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.True(actual.ReadOnly);
        Assert.True(actual.ReadOnlyLocked);
    }

    [Fact]
    public void Info_ReadOnlyFalseOnRemoteAccount_IsWritable()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Remote, readOnly: false);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.False(actual.IsLocal);
        Assert.False(actual.ReadOnly);
        Assert.False(actual.ReadOnlyLocked);
    }

    [Fact]
    public void Info_LocalAccount_HasHostAndPortButNotPathOrKey()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.Equal("127.0.0.1:1", actual.Endpoint);
        Assert.DoesNotContain(TestConnectionStrings.EmulatorKey, actual.ToString());
    }

    // ---- a connection typed in the page ----

    [Fact]
    public async Task UseAsync_RemoteAccountWithoutAllowWrites_IsReadOnly()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var client = new StubClient(TestConnectionStrings.Remote);

        // Act
        await connection.UseAsync(client, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.IsCustom);
        Assert.False(connection.Info.IsLocal);
        Assert.True(connection.Info.ReadOnly);
        Assert.False(connection.Info.ReadOnlyLocked);
    }

    [Fact]
    public async Task UseAsync_RemoteAccountWithAllowWrites_IsWritable()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var client = new StubClient(TestConnectionStrings.Remote);

        // Act
        await connection.UseAsync(client, allowWrites: true, default);

        // Assert
        Assert.False(connection.Info.ReadOnly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseAsync_LocalAccount_IsWritableWhateverAllowWritesSays(bool allowWrites)
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Remote);
        var client = new StubClient(TestConnectionStrings.Local);

        // Act
        await connection.UseAsync(client, allowWrites, default);

        // Assert
        Assert.True(connection.Info.IsLocal);
        Assert.False(connection.Info.ReadOnly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseAsync_RemoteAccountWhenLockedByAppHost_StaysReadOnlyAndLocked(bool allowWrites)
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local, readOnly: true);
        var client = new StubClient(TestConnectionStrings.Remote);

        // Act
        await connection.UseAsync(client, allowWrites, default);

        // Assert
        Assert.True(connection.Info.ReadOnly);
        Assert.True(connection.Info.ReadOnlyLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UseAsync_LocalAccountWhenLockedByAppHost_StaysReadOnly(bool allowWrites)
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local, readOnly: true);
        var client = new StubClient(TestConnectionStrings.Local);

        // Act
        await connection.UseAsync(client, allowWrites, default);

        // Assert
        Assert.True(connection.Info.ReadOnly);
    }

    [Fact]
    public async Task UseAsync_ReadOnlyFalseInAppHostAndRemoteAccountWithoutAllowWrites_IsReadOnly()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local, readOnly: false);
        var client = new StubClient(TestConnectionStrings.Remote);

        // Act
        await connection.UseAsync(client, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.ReadOnly);
    }

    [Fact]
    public async Task UseAsync_NewClient_ChecksItByListingContainersBeforeUsingIt()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var client = new StubClient(TestConnectionStrings.Remote);

        // Act
        await connection.UseAsync(client, allowWrites: false, default);

        // Assert
        Assert.Equal(1, client.Listed);
        Assert.Same(client, connection.Client);
    }

    [Fact]
    public async Task UseAsync_ClientThatFailsTheCheck_KeepsActiveConnection()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var before = connection.Client;
        var failure = new RequestFailedException(403, "Server failed to authenticate the request.", "AuthenticationFailed", null);
        var client = new StubClient(TestConnectionStrings.Remote, failure);

        // Act
        var thrown = await Assert.ThrowsAsync<RequestFailedException>(() =>
            connection.UseAsync(client, allowWrites: true, default));

        // Assert
        Assert.Same(failure, thrown);
        Assert.Same(before, connection.Client);
        Assert.False(connection.Info.IsCustom);
        Assert.Equal("devstoreaccount1", connection.Info.AccountName);
    }

    [Fact]
    public async Task Reset_AfterUseAsync_GoesBackToAppHostConnection()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Remote);
        var original = connection.Client;
        await connection.UseAsync(new StubClient(TestConnectionStrings.Local), allowWrites: false, default);

        // Act
        connection.Reset();

        // Assert
        Assert.Same(original, connection.Client);
        Assert.False(connection.Info.IsCustom);
        Assert.Equal("prodaccount", connection.Info.AccountName);
        Assert.True(connection.Info.ReadOnly);
    }

    private static StorageConnection Create(string connectionString, bool? readOnly = null) =>
        new(Options.Create(new StorageExplorerOptions { ConnectionString = connectionString, ReadOnly = readOnly }));

    /// <summary>A client whose container listing answers with an empty page, or fails, without any network.</summary>
    private sealed class StubClient(string connectionString, Exception? failure = null) : BlobServiceClient(connectionString)
    {
        public int Listed { get; private set; }

        // The SDK has two overloads and which one a call binds to is an accident of optional parameters, so both are
        // covered: a call that got past this stub would go to the network.
        public override AsyncPageable<BlobContainerItem> GetBlobContainersAsync(
            BlobContainerTraits traits = BlobContainerTraits.None,
            string? prefix = null,
            CancellationToken cancellationToken = default) => List();

        public override AsyncPageable<BlobContainerItem> GetBlobContainersAsync(
            BlobContainerTraits traits,
            BlobContainerStates states,
            string? prefix,
            CancellationToken cancellationToken = default) => List();

        private AsyncPageable<BlobContainerItem> List()
        {
            Listed++;

            return failure is null
                ? AsyncPageable<BlobContainerItem>.FromPages([Page<BlobContainerItem>.FromValues([], null, null!)])
                : new FailingPageable(failure);
        }
    }

    private sealed class FailingPageable(Exception failure) : AsyncPageable<BlobContainerItem>
    {
        public override async IAsyncEnumerable<Page<BlobContainerItem>> AsPages(
            string? continuationToken = null,
            int? pageSizeHint = null)
        {
            await Task.Yield();
            throw failure;
#pragma warning disable CS0162 // Makes this an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }
}
