using System.Text;
using Azure.Storage.Queues.Models;

namespace StorageExplorer.Web.Queues;

internal interface IQueueExplorerService
{
    Task<IReadOnlyList<QueueSummary>> ListQueuesAsync(CancellationToken cancellationToken);

    /// <returns>
    /// Up to <see cref="QueueExplorerService.MaxPeekedMessages"/> messages, without changing them: a peek does not
    /// hide a message from a consumer or raise its <c>DequeueCount</c>, unlike receiving one.
    /// </returns>
    Task<IReadOnlyList<QueueMessage>> PeekMessagesAsync(string queue, CancellationToken cancellationToken);

    /// <returns><c>true</c> if the queue existed and was deleted, <c>false</c> if it did not exist.</returns>
    Task<bool> DeleteQueueAsync(string queue, CancellationToken cancellationToken);

    /// <summary>Removes every message in the queue, not just the ones a peek would show.</summary>
    Task ClearQueueAsync(string queue, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the messages a peek would show right now (up to <see cref="QueueExplorerService.MaxPeekedMessages"/>),
    /// and only those: it peeks again immediately before receiving, and only deletes a received message whose ID was
    /// in that fresh peek, releasing anything else received unharmed. This keeps the window in which a message the
    /// user never saw could get swept up and deleted down to the gap between those two calls, instead of however
    /// long the page had been open. A message that falls in that gap still gets its <c>DequeueCount</c> raised even
    /// though it is released rather than deleted — receiving is the only way to get the pop receipt delete requires,
    /// and it cannot be limited to specific message IDs, so this is the closest this can get to exact.
    /// </summary>
    /// <returns>How many messages were actually deleted.</returns>
    Task<int> DeletePeekedMessagesAsync(string queue, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the queue. Unlike <c>BlobExplorerService.CreateContainerAsync</c>, an already-existing queue does not
    /// make this throw: Azure Queue's Create Queue operation succeeds as a no-op when the existing queue's metadata
    /// matches what is given here (none, since this never sets any), only throwing when it differs.
    /// </summary>
    Task CreateQueueAsync(string queue, CancellationToken cancellationToken);

    /// <summary>
    /// Enqueues <paramref name="text"/> exactly as given, with no encoding applied: this explorer reads queues the
    /// same way (see <see cref="QueueMessage.Text"/>), so a plain message sent here round-trips as plain text on the
    /// next peek instead of showing up Base64-encoded.
    /// </summary>
    Task SendMessageAsync(string queue, string text, CancellationToken cancellationToken);
}

internal sealed class QueueExplorerService(IStorageConnection connection) : IQueueExplorerService
{
    // Azure refuses a peek for more than this; it is also few enough to show without paging.
    internal const int MaxPeekedMessages = 32;

    // How many queues get GetPropertiesAsync (for the message count) at the same time.
    private const int MaxConcurrentPropertyReads = 16;

    // Bytes that are not valid UTF-8 mean a Base64 decoding landed on something else (binary data), so this must not
    // silently replace them with a placeholder the way Encoding.UTF8 does.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public async Task<IReadOnlyList<QueueSummary>> ListQueuesAsync(CancellationToken cancellationToken)
    {
        var names = new List<string>();

        await foreach (var queue in connection.Queue!.GetQueuesAsync(cancellationToken: cancellationToken))
            names.Add(queue.Name);

        // The listing itself does not carry the message count; that takes its own request per queue.
        var summaries = new QueueSummary[names.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, names.Count),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentPropertyReads, CancellationToken = cancellationToken },
            async (index, token) =>
            {
                var properties = await connection.Queue!.GetQueueClient(names[index]).GetPropertiesAsync(token);
                summaries[index] = new QueueSummary(names[index], properties.Value.ApproximateMessagesCountLong);
            });

        return summaries;
    }

    public async Task<IReadOnlyList<QueueMessage>> PeekMessagesAsync(string queue, CancellationToken cancellationToken)
    {
        var response = await connection.Queue!.GetQueueClient(queue).PeekMessagesAsync(MaxPeekedMessages, cancellationToken);

        return response.Value.Select(ToQueueMessage).ToArray();
    }

    public async Task<bool> DeleteQueueAsync(string queue, CancellationToken cancellationToken)
    {
        var response = await connection.Queue!.GetQueueClient(queue).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        return response.Value;
    }

    public Task ClearQueueAsync(string queue, CancellationToken cancellationToken) =>
        connection.Queue!.GetQueueClient(queue).ClearMessagesAsync(cancellationToken);

    public async Task<int> DeletePeekedMessagesAsync(string queue, CancellationToken cancellationToken)
    {
        var queueClient = connection.Queue!.GetQueueClient(queue);

        var peeked = await queueClient.PeekMessagesAsync(MaxPeekedMessages, cancellationToken);
        var targetIds = peeked.Value.Select(message => message.MessageId).ToHashSet();

        var received = await queueClient.ReceiveMessagesAsync(MaxPeekedMessages, cancellationToken: cancellationToken);

        var deleted = 0;
        foreach (var message in received.Value)
        {
            if (targetIds.Contains(message.MessageId))
            {
                await queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                deleted++;
            }
            else
            {
                await queueClient.UpdateMessageAsync(
                    message.MessageId, message.PopReceipt, visibilityTimeout: TimeSpan.Zero, cancellationToken: cancellationToken);
            }
        }

        return deleted;
    }

    public Task CreateQueueAsync(string queue, CancellationToken cancellationToken) =>
        connection.Queue!.GetQueueClient(queue).CreateAsync(cancellationToken: cancellationToken);

    public Task SendMessageAsync(string queue, string text, CancellationToken cancellationToken) =>
        connection.Queue!.GetQueueClient(queue).SendMessageAsync(text, cancellationToken);

    private static QueueMessage ToQueueMessage(PeekedMessage message)
    {
        var (text, wasDecoded) = DecodeText(message.MessageText);

        return new QueueMessage(
            message.MessageId, text, wasDecoded, message.MessageText, message.InsertedOn, message.ExpiresOn, message.DequeueCount);
    }

    internal static (string Text, bool WasBase64Decoded) DecodeText(string raw) =>
        raw.Length > 0 && TryDecodeBase64Utf8(raw, out var decoded) ? (decoded, true) : (raw, false);

    private static bool TryDecodeBase64Utf8(string raw, out string decoded)
    {
        // The decoded form is never longer than the encoded one.
        var buffer = new byte[raw.Length];

        if (!Convert.TryFromBase64String(raw, buffer, out var written))
        {
            decoded = "";
            return false;
        }

        try
        {
            decoded = StrictUtf8.GetString(buffer, 0, written);
            return true;
        }
        catch (DecoderFallbackException)
        {
            decoded = "";
            return false;
        }
    }
}
