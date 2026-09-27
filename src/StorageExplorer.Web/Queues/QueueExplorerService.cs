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
