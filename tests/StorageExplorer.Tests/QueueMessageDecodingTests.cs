using System.Text;
using StorageExplorer.Web.Queues;

namespace StorageExplorer.Web.Tests;

/// <summary>
/// The heuristic that decides whether a message's text is Base64 underneath. It only computes strings, so it needs no
/// network or queue.
/// </summary>
public class QueueMessageDecodingTests
{
    [Fact]
    public void DecodeText_Base64OfUtf8Text_ReturnsDecodedTextAndFlagsIt()
    {
        // Arrange
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello from a function"));

        // Act
        var (text, wasDecoded) = QueueExplorerService.DecodeText(encoded);

        // Assert
        Assert.Equal("hello from a function", text);
        Assert.True(wasDecoded);
    }

    [Theory]
    [InlineData("hello world")] // A space is not in the Base64 alphabet.
    [InlineData("not base64 at all!!")]
    [InlineData("こんにちは")] // Outside the Base64 alphabet too.
    [InlineData("")] // Technically valid (empty) Base64, but decoding it reveals nothing, so it is left alone.
    public void DecodeText_TextThatIsNotUsefullyBase64_IsLeftAsIs(string raw)
    {
        // Act
        var (text, wasDecoded) = QueueExplorerService.DecodeText(raw);

        // Assert
        Assert.Equal(raw, text);
        Assert.False(wasDecoded);
    }

    [Fact]
    public void DecodeText_Base64OfBytesThatAreNotValidUtf8_IsLeftAsIs()
    {
        // Arrange
        // 0xFF is never valid in UTF-8; decoding this would corrupt it (replacement characters) rather than reveal text.
        var encoded = Convert.ToBase64String([0xFF, 0xFE, 0x00, 0x01]);

        // Act
        var (text, wasDecoded) = QueueExplorerService.DecodeText(encoded);

        // Assert
        Assert.Equal(encoded, text);
        Assert.False(wasDecoded);
    }
}
