using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// The queue routes over HTTP with nothing replaced: real routes, real connection, real Azurite. Complements
/// <see cref="QueueExplorerServiceTests"/> (the service on its own) by proving the route, the DI wiring and the JSON
/// shape all agree.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class QueueApiTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestQueue queue = null!;
    private readonly List<IDisposable> disposables = [];

    public async Task InitializeAsync()
    {
        queue = await TestQueue.CreateAsync(azurite);
        await queue.SendAsync("hello world");
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in disposables)
            disposable.Dispose();

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task GetQueues_ExistingQueue_ListsItWithMessageCount()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/queues");

        // Assert
        var listed = Assert.Single(actual.EnumerateArray(), q => q.GetProperty("name").GetString() == queue.Name);
        Assert.Equal(1, listed.GetProperty("approximateMessageCount").GetInt64());
    }

    [Fact]
    public async Task GetMessages_ExistingQueue_ReturnsThePeekedMessage()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/queues/{queue.Name}/messages");

        // Assert
        var message = Assert.Single(actual.EnumerateArray());
        Assert.Equal("hello world", message.GetProperty("text").GetString());
        Assert.False(message.GetProperty("textWasBase64Decoded").GetBoolean());
    }

    [Fact]
    public async Task GetMessages_MissingQueue_ReturnsStorageError()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.GetAsync("/api/queues/no-such-queue/messages");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Storage request failed", problem.GetProperty("title").GetString());
        Assert.StartsWith("QueueNotFound", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PostMessage_Text_EnqueuesItAndShowsUpInPeek()
    {
        // Arrange
        using var client = Start();
        var request = WithHeader(HttpMethod.Post, $"/api/queues/{queue.Name}/messages");
        request.Content = new StringContent("a brand new message");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/queues/{queue.Name}/messages");
        Assert.Contains(messages.EnumerateArray(), m => m.GetProperty("text").GetString() == "a brand new message");
    }

    [Fact]
    public async Task PostMessage_WithoutExplorerHeader_SendsNothing()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.PostAsync($"/api/queues/{queue.Name}/messages", new StringContent("should not land"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var messages = await client.GetFromJsonAsync<JsonElement>($"/api/queues/{queue.Name}/messages");
        Assert.DoesNotContain(messages.EnumerateArray(), m => m.GetProperty("text").GetString() == "should not land");
    }

    [Fact]
    public async Task DeleteQueue_ExistingQueue_ReturnsNoContentAndRemovesIt()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.SendAsync(WithHeader(HttpMethod.Delete, $"/api/queues/{queue.Name}"));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listing = await client.GetFromJsonAsync<JsonElement>("/api/queues");
        Assert.DoesNotContain(listing.EnumerateArray(), q => q.GetProperty("name").GetString() == queue.Name);
    }

    [Fact]
    public async Task ClearQueue_QueueWithMessages_RemovesEvenMessagesBeyondThePeekWindow()
    {
        // Arrange
        for (var i = 0; i < 40; i++)
            await queue.SendAsync($"extra {i}");
        using var client = Start();

        // Act
        var response = await client.SendAsync(WithHeader(HttpMethod.Delete, $"/api/queues/{queue.Name}/messages"));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/queues");
        var summary = Assert.Single(listed.EnumerateArray(), q => q.GetProperty("name").GetString() == queue.Name);
        Assert.Equal(0, summary.GetProperty("approximateMessageCount").GetInt64());
    }

    [Fact]
    public async Task DeletePeekedMessages_QueueWithOneMessage_ReturnsHowManyWereDeleted()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.SendAsync(WithHeader(HttpMethod.Delete, $"/api/queues/{queue.Name}/messages/peeked"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, body.GetProperty("deleted").GetInt32());
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/queues/{queue.Name}/messages")).EnumerateArray());
    }

    [Fact]
    public async Task DeletePeekedMessages_WithoutExplorerHeader_DeletesNothing()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.DeleteAsync($"/api/queues/{queue.Name}/messages/peeked");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/queues/{queue.Name}/messages");
        Assert.Single(actual.EnumerateArray());
    }

    [Fact]
    public async Task GetQueues_ConnectionWithNoQueueEndpoint_ReturnsBadRequest()
    {
        // Arrange
        // A blob container SAS: no AccountName, so a queue client cannot be built from it at all. This never touches
        // the network (building the connection only parses the string), so it does not need Azurite to be reachable.
        const string blobSasOnly =
            "BlobEndpoint=https://acct.blob.example.invalid/;" +
            "SharedAccessSignature=sv=2021-08-06&ss=b&srt=sco&sp=r&se=2100-01-01&st=2020-01-01&spr=https&sig=abc";
        using var client = Start(blobSasOnly);

        // Act
        var response = await client.GetAsync("/api/queues");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No queue endpoint", problem.GetProperty("title").GetString());
    }

    private static HttpRequestMessage WithHeader(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Storage-Explorer", "1");
        return request;
    }

    private HttpClient Start(string? connectionString = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("StorageExplorer:ConnectionString", connectionString ?? azurite.ConnectionString);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IContainerEnvironment>();
                services.AddSingleton<IContainerEnvironment, NotInContainer>();
            });
        });
        var client = factory.CreateClient();

        disposables.Add(client);
        disposables.Add(factory);

        return client;
    }
}

file sealed class NotInContainer : IContainerEnvironment
{
    public bool RunningInContainer => false;
}
