namespace StorageExplorer.Web.Queues;

internal sealed record QueueSummary(string Name, long ApproximateMessageCount);

/// <param name="Text">
/// The message text, or its Base64 decoding when it round-trips to valid UTF-8 text (see
/// <paramref name="TextWasBase64Decoded"/>). This explorer reads queues with no message encoding applied, so a
/// message written Base64-encoded (the classic SDK always did, and Functions triggers and other languages often
/// still do) otherwise shows up looking like Base64 rather than the content it carries.
/// </param>
/// <param name="RawText">
/// The message text exactly as the queue holds it, before the Base64 decoding above. Equal to <paramref name="Text"/>
/// when nothing was decoded.
/// </param>
internal sealed record QueueMessage(
    string MessageId,
    string Text,
    bool TextWasBase64Decoded,
    string RawText,
    DateTimeOffset? InsertedOn,
    DateTimeOffset? ExpiresOn,
    long DequeueCount);
