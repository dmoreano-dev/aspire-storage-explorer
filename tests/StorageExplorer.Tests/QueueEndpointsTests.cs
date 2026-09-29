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

    // ---- create queue ----

    [Fact]
    public async Task CreateQueue_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/queues/orders");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createQueue:orders"], host.Queues.Calls);
    }

    [Fact]
    public async Task CreateQueue_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/queues/orders");

        // Act
        var response = await host.Client.SendAsync(request);

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

    // ---- send message ----

    [Fact]
    public async Task PostMessage_Content_SendsItAndReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/queues/orders/messages");
        request.Content = new StringContent("hello world");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["sendMessage:orders:hello world"], host.Queues.Calls);
    }

    [Fact]
    public async Task PostMessage_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/queues/orders/messages");
        request.Content = new StringContent("hello world");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No queue endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Queues.Calls);
    }

    // ---- delete queue ----

    [Fact]
    public async Task DeleteQueue_ExistingQueue_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["deleteQueue:orders"], host.Queues.Calls);
    }

    [Fact]
    public async Task DeleteQueue_MissingQueue_ReturnsNotFound()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        host.Queues.DeleteQueueResult = false;
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteQueue_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No queue endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Queues.Calls);
    }

    // ---- clear queue ----

    [Fact]
    public async Task ClearQueue_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders/messages");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["clearQueue:orders"], host.Queues.Calls);
    }

    [Fact]
    public async Task ClearQueue_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders/messages");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Queues.Calls);
    }

    // ---- delete peeked messages ----

    [Fact]
    public async Task DeletePeekedMessages_ReturnsHowManyWereDeleted()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Queue = new QueueServiceClient(TestConnectionStrings.Local);
        host.Queues.DeletedPeekedCount = 5;
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders/messages/peeked");

        // Act
        var response = await host.Client.SendAsync(request);
        var actual = await Json(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["deletePeeked:orders"], host.Queues.Calls);
        Assert.Equal(5, actual.GetProperty("deleted").GetInt32());
    }

    [Fact]
    public async Task DeletePeekedMessages_NoQueueEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/queues/orders/messages/peeked");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
