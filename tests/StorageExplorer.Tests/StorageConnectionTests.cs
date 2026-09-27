using Azure;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
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

    // ---- queues and tables ----

    [Fact]
    public void Info_ConnectionStringWithoutAccountName_HasNoQueuesOrTables()
    {
        // Arrange
        // A blob container SAS carries no AccountName, so a queue or table client cannot be built from it at all.
        var connection = Create(TestConnectionStrings.BlobSasOnly);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.False(actual.HasQueues);
        Assert.False(actual.HasTables);
    }

    [Fact]
    public void Info_ConnectionStringWithAccountName_HasQueuesAndTables()
    {
        // Arrange
        // AccountName lets a queue and table client be built even without their own explicit endpoint (the SDK
        // derives one); the AppHost connection is never probed over the network at startup, so this only says a
        // client could be built, not that the account actually offers the service.
        var connection = Create(TestConnectionStrings.Local);

        // Act
        var actual = connection.Info;

        // Assert
        Assert.True(actual.HasQueues);
        Assert.True(actual.HasTables);
    }

    [Fact]
    public async Task UseAsync_QueueAndTableThatListSuccessfully_SetsHasQueuesAndHasTablesTrue()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var clients = new StorageClients(
            new StubClient(TestConnectionStrings.Remote),
            new StubQueueClient(TestConnectionStrings.Remote),
            new StubTableClient(TestConnectionStrings.Remote));

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.HasQueues);
        Assert.True(connection.Info.HasTables);
    }

    [Fact]
    public async Task UseAsync_NullQueueAndTableClients_SetsHasQueuesAndHasTablesFalse()
    {
        // Arrange
        // A client that could not be built at all (a connection string with no AccountName) never gets probed.
        var connection = Create(TestConnectionStrings.Local);
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.False(connection.Info.HasQueues);
        Assert.False(connection.Info.HasTables);
    }

    [Fact]
    public async Task UseAsync_QueueThatFailsToList_SetsHasQueuesFalseButStillSwitchesConnection()
    {
        // Arrange
        // An account kind that has no queues (or one the credentials cannot reach) fails the probe, but that is not
        // reason enough to refuse the whole connection: only the blob check is mandatory.
        var connection = Create(TestConnectionStrings.Local);
        var queue = new StubQueueClient(TestConnectionStrings.Remote, new RequestFailedException(403, "nope"));
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), queue, null);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.IsCustom);
        Assert.False(connection.Info.HasQueues);
    }

    [Fact]
    public async Task UseAsync_TableThatFailsToList_SetsHasTablesFalseButStillSwitchesConnection()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var table = new StubTableClient(TestConnectionStrings.Remote, new RequestFailedException(403, "nope"));
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, table);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.IsCustom);
        Assert.False(connection.Info.HasTables);
    }

    [Fact]
    public async Task UseAsync_BlobCheckFails_NeverListsQueuesOrTables()
    {
        // Arrange
        // The blob check runs first and is the one that decides whether the switch happens at all; queues and
        // tables are only worth probing once that passed.
        var connection = Create(TestConnectionStrings.Local);
        var failure = new RequestFailedException(403, "Server failed to authenticate the request.", "AuthenticationFailed", null);
        var queue = new StubQueueClient(TestConnectionStrings.Remote);
        var table = new StubTableClient(TestConnectionStrings.Remote);
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote, failure), queue, table);

        // Act
        await Assert.ThrowsAsync<RequestFailedException>(() => connection.UseAsync(clients, allowWrites: true, default));

        // Assert
        Assert.Equal(0, queue.Listed);
        Assert.Equal(0, table.Listed);
    }

    // ---- a connection typed in the page ----

    [Fact]
    public async Task UseAsync_RemoteAccountWithoutAllowWrites_IsReadOnly()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

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
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites: true, default);

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
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Local), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites, default);

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
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites, default);

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
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Local), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites, default);

        // Assert
        Assert.True(connection.Info.ReadOnly);
    }

    [Fact]
    public async Task UseAsync_ReadOnlyFalseInAppHostAndRemoteAccountWithoutAllowWrites_IsReadOnly()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local, readOnly: false);
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote), null, null);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.True(connection.Info.ReadOnly);
    }

    [Fact]
    public async Task UseAsync_NewClient_ChecksItByListingContainersBeforeUsingIt()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var client = new StubClient(TestConnectionStrings.Remote);
        var clients = new StorageClients(client, null, null);

        // Act
        await connection.UseAsync(clients, allowWrites: false, default);

        // Assert
        Assert.Equal(1, client.Listed);
        Assert.Same(client, connection.Blob);
    }

    [Fact]
    public async Task UseAsync_ClientThatFailsTheCheck_KeepsActiveConnection()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Local);
        var before = connection.Blob;
        var failure = new RequestFailedException(403, "Server failed to authenticate the request.", "AuthenticationFailed", null);
        var clients = new StorageClients(new StubClient(TestConnectionStrings.Remote, failure), null, null);

        // Act
        var thrown = await Assert.ThrowsAsync<RequestFailedException>(() =>
            connection.UseAsync(clients, allowWrites: true, default));

        // Assert
        Assert.Same(failure, thrown);
        Assert.Same(before, connection.Blob);
        Assert.False(connection.Info.IsCustom);
        Assert.Equal("devstoreaccount1", connection.Info.AccountName);
    }

    [Fact]
    public async Task Reset_AfterUseAsync_GoesBackToAppHostConnection()
    {
        // Arrange
        var connection = Create(TestConnectionStrings.Remote);
        var original = connection.Blob;
        await connection.UseAsync(new StorageClients(new StubClient(TestConnectionStrings.Local), null, null), allowWrites: false, default);

        // Act
        connection.Reset();

        // Assert
        Assert.Same(original, connection.Blob);
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
                : new FailingPageable<BlobContainerItem>(failure);
        }
    }

    /// <summary>A client whose queue listing answers with an empty page, or fails, without any network.</summary>
    private sealed class StubQueueClient(string connectionString, Exception? failure = null) : QueueServiceClient(connectionString)
    {
        public int Listed { get; private set; }

        public override AsyncPageable<QueueItem> GetQueuesAsync(
            QueueTraits traits = QueueTraits.None,
            string? prefix = null,
            CancellationToken cancellationToken = default)
        {
            Listed++;

            return failure is null
                ? AsyncPageable<QueueItem>.FromPages([Page<QueueItem>.FromValues([], null, null!)])
                : new FailingPageable<QueueItem>(failure);
        }
    }

    /// <summary>A client whose table listing answers with an empty page, or fails, without any network.</summary>
    private sealed class StubTableClient(string connectionString, Exception? failure = null) : TableServiceClient(connectionString)
    {
        public int Listed { get; private set; }

        public override AsyncPageable<TableItem> QueryAsync(
            string? filter = null,
            int? maxPerPage = null,
            CancellationToken cancellationToken = default)
        {
            Listed++;

            return failure is null
                ? AsyncPageable<TableItem>.FromPages([Page<TableItem>.FromValues([], null, null!)])
                : new FailingPageable<TableItem>(failure);
        }
    }

    private sealed class FailingPageable<T>(Exception failure) : AsyncPageable<T> where T : notnull
    {
        public override async IAsyncEnumerable<Page<T>> AsPages(
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
