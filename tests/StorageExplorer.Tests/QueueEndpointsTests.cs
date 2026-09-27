using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Storage.Queues;
using StorageExplorer.Web.Queues;

namespace StorageExplorer.Web.Tests;

public class QueueEndpointsTests
{
    // ---- queues ----

    [Fact]
    public async Task GetQueues_TwoQueues_ReturnsBothNamesAndCounts()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        host.Queues.Queues =
        [
            new QueueSummary("orders", 3),
            new QueueSummary("notifications", 0),
        ];

        // Act
        var response = await host.Client.GetAsync("/api/queues");
        var actual = await Json(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["orders", "notifications"], actual.EnumerateArray().Select(q => q.GetProperty("name").GetString()));
        Assert.Equal(3, actual[0].GetProperty("approximateMessageCount").GetInt64());
    }

    [Fact]
    public async Task GetQueues_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/queues");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No queue endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Queues.Calls);
    }

    // ---- messages ----

    [Fact]
    public async Task GetMessages_Queue_ReturnsMessages()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        host.Queues.Messages =
        [
            new QueueMessage("id-1", "hello", false, "hello", DateTimeOffset.Parse("2026-01-02T03:04:05Z"), null, 0),
        ];

        // Act
        var response = await host.Client.GetAsync("/api/queues/orders/messages");
        var actual = await Json(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["messages:orders"], host.Queues.Calls);
        var message = Assert.Single(actual.EnumerateArray());
        Assert.Equal("hello", message.GetProperty("text").GetString());
        Assert.False(message.GetProperty("textWasBase64Decoded").GetBoolean());
    }

    [Fact]
    public async Task GetMessages_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/queues/orders/messages");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No queue endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Queues.Calls);
    }

    // ---- these routes never change data, so the read-only guards do not apply to them ----

    [Fact]
    public async Task GetQueues_LockedExplorer_StillLists()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);

        // Act
        var response = await host.Client.GetAsync("/api/queues");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
