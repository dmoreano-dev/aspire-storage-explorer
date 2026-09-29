using System.Text;
using Azure;
using StorageExplorer.Web.Queues;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>The service against a real Azurite: what the emulator does is what the tests believe.</summary>
[Collection(AzuriteCollection.Name)]
public sealed class QueueExplorerServiceTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestQueue queue = null!;
    private QueueExplorerService service = null!;

    public async Task InitializeAsync()
    {
        queue = await TestQueue.CreateAsync(azurite);
        service = queue.Service();
    }

    public async Task DisposeAsync() => await queue.DisposeAsync();

    // ---- queues ----

    [Fact]
    public async Task ListQueuesAsync_EmptyQueue_ReturnsItWithZeroMessages()
    {
        // Act
        var actual = await service.ListQueuesAsync(default);

        // Assert
        var listed = Assert.Single(actual, q => q.Name == queue.Name);
        Assert.Equal(0, listed.ApproximateMessageCount);
    }

    [Fact]
    public async Task ListQueuesAsync_QueueWithMessages_ReturnsApproximateMessageCount()
    {
        // Arrange
        await queue.SendAsync("a");
        await queue.SendAsync("b");
        await queue.SendAsync("c");

        // Act
        var actual = await service.ListQueuesAsync(default);

        // Assert
        var listed = Assert.Single(actual, q => q.Name == queue.Name);
        Assert.Equal(3, listed.ApproximateMessageCount);
    }

    // ---- messages ----

    [Fact]
    public async Task PeekMessagesAsync_PlainTextMessage_ReturnsItUndecoded()
    {
        // Arrange
        await queue.SendAsync("hello world");

        // Act
        var actual = await service.PeekMessagesAsync(queue.Name, default);

        // Assert
        var message = Assert.Single(actual);
        Assert.Equal("hello world", message.Text);
        Assert.False(message.TextWasBase64Decoded);
        Assert.Equal("hello world", message.RawText);
    }

    [Fact]
    public async Task PeekMessagesAsync_Base64EncodedMessage_DecodesIt()
    {
        // Arrange
        // What another SDK, or an Azure Functions queue trigger, typically leaves behind.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from a function"));
        await queue.SendAsync(encoded);

        // Act
        var actual = await service.PeekMessagesAsync(queue.Name, default);

        // Assert
        var message = Assert.Single(actual);
        Assert.Equal("hello from a function", message.Text);
        Assert.True(message.TextWasBase64Decoded);
        Assert.Equal(encoded, message.RawText);
    }

    [Fact]
    public async Task PeekMessagesAsync_DoesNotHideTheMessageOrRaiseDequeueCount()
    {
        // Arrange
        await queue.SendAsync("still here");

        // Act
        await service.PeekMessagesAsync(queue.Name, default);
        var actual = await service.PeekMessagesAsync(queue.Name, default);

        // Assert
        var message = Assert.Single(actual);
        Assert.Equal(0, message.DequeueCount);
    }

    [Fact]
    public async Task PeekMessagesAsync_Message_HasInsertedAndExpiresOn()
    {
        // Arrange
        await queue.SendAsync("with dates");

        // Act
        var actual = await service.PeekMessagesAsync(queue.Name, default);

        // Assert
        var message = Assert.Single(actual);
        Assert.NotNull(message.InsertedOn);
        Assert.NotNull(message.ExpiresOn);
        Assert.True(message.ExpiresOn > message.InsertedOn);
    }

    [Fact]
    public async Task PeekMessagesAsync_MoreThanTheMax_ReturnsOnlyTheMax()
    {
        // Arrange
        for (var i = 0; i < QueueExplorerService.MaxPeekedMessages + 5; i++)
            await queue.SendAsync($"message {i}");

        // Act
        var actual = await service.PeekMessagesAsync(queue.Name, default);

        // Assert
        Assert.Equal(QueueExplorerService.MaxPeekedMessages, actual.Count);
    }

    [Fact]
    public async Task PeekMessagesAsync_MissingQueue_ThrowsQueueNotFound()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.PeekMessagesAsync("no-such-queue", default));

        // Assert
        Assert.Equal(404, actual.Status);
        Assert.Equal("QueueNotFound", actual.ErrorCode);
    }

    // ---- delete queue ----

    [Fact]
    public async Task DeleteQueueAsync_ExistingQueue_ReturnsTrueAndDeletesIt()
    {
        // Act
        var actual = await service.DeleteQueueAsync(queue.Name, default);

        // Assert
        Assert.True(actual);
        await Assert.ThrowsAsync<RequestFailedException>(() => service.PeekMessagesAsync(queue.Name, default));
    }

    [Fact]
    public async Task DeleteQueueAsync_MissingQueue_ReturnsFalse()
    {
        // Act
        var actual = await service.DeleteQueueAsync("no-such-queue", default);

        // Assert
        Assert.False(actual);
    }

    // ---- clear queue ----

    [Fact]
    public async Task ClearQueueAsync_QueueWithMessages_RemovesAllOfThem()
    {
        // Arrange
        for (var i = 0; i < QueueExplorerService.MaxPeekedMessages + 5; i++)
            await queue.SendAsync($"message {i}");

        // Act
        await service.ClearQueueAsync(queue.Name, default);

        // Assert
        Assert.Empty(await service.PeekMessagesAsync(queue.Name, default));
    }

    [Fact]
    public async Task ClearQueueAsync_MissingQueue_ThrowsQueueNotFound()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.ClearQueueAsync("no-such-queue", default));

        // Assert
        Assert.Equal(404, actual.Status);
        Assert.Equal("QueueNotFound", actual.ErrorCode);
    }

    // ---- delete peeked messages ----

    [Fact]
    public async Task DeletePeekedMessagesAsync_FewerThanTheMax_DeletesAllOfThemAndReturnsTheCount()
    {
        // Arrange
        await queue.SendAsync("a");
        await queue.SendAsync("b");
        await queue.SendAsync("c");

        // Act
        var actual = await service.DeletePeekedMessagesAsync(queue.Name, default);

        // Assert
        Assert.Equal(3, actual);
        Assert.Empty(await service.PeekMessagesAsync(queue.Name, default));
    }

    [Fact]
    public async Task DeletePeekedMessagesAsync_MoreThanTheMax_DeletesOnlyTheFrontBatchAndLeavesTheRest()
    {
        // Arrange
        var total = QueueExplorerService.MaxPeekedMessages + 5;
        for (var i = 0; i < total; i++)
            await queue.SendAsync($"message {i}");

        // Act
        var actual = await service.DeletePeekedMessagesAsync(queue.Name, default);

        // Assert
        Assert.Equal(QueueExplorerService.MaxPeekedMessages, actual);
        var listed = Assert.Single(await service.ListQueuesAsync(default), q => q.Name == queue.Name);
        Assert.Equal(5, listed.ApproximateMessageCount);
    }

    [Fact]
    public async Task DeletePeekedMessagesAsync_EmptyQueue_ReturnsZero()
    {
        // Act
        var actual = await service.DeletePeekedMessagesAsync(queue.Name, default);

        // Assert
        Assert.Equal(0, actual);
    }

    // ---- create queue ----

    [Fact]
    public async Task CreateQueueAsync_NewName_CreatesQueue()
    {
        // Arrange
        var name = $"t{Guid.NewGuid():N}";
        var client = new Azure.Storage.Queues.QueueServiceClient(azurite.ConnectionString).GetQueueClient(name);

        try
        {
            // Act
            await service.CreateQueueAsync(name, default);

            // Assert
            Assert.True(await client.ExistsAsync());
        }
        finally
        {
            await client.DeleteIfExistsAsync();
        }
    }

    [Fact]
    public async Task CreateQueueAsync_ExistingQueue_CompletesWithoutThrowing()
    {
        // Act
        // Unlike a blob container, the Queue service's Create Queue is idempotent when the existing queue's metadata
        // matches what is given (none, here) - not throwing is the assertion.
        await service.CreateQueueAsync(queue.Name, default);
    }
}
