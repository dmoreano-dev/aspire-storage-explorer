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
/// The whole app over HTTP with nothing replaced: real routes, real filters, real connection, real Azurite. This is
/// what the page talks to.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class ApiTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestContainer container = null!;
    private readonly List<IDisposable> disposables = [];

    public async Task InitializeAsync()
    {
        container = await TestContainer.CreateAsync(azurite);

        await Task.WhenAll(
            container.Upload("readme.txt", "hello", "text/plain"),
            container.Upload("2025/q1/report.csv", "a,b\n1,2\n", "text/csv"),
            container.Upload("2025/notes.txt", "notes", "text/plain"));
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in disposables)
            disposable.Dispose();

        await container.DisposeAsync();
    }

    // ---- reading ----

    [Fact]
    public async Task GetConnection_LocalEmulator_ReturnsLocalWritableAccount()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("devstoreaccount1", actual.GetProperty("accountName").GetString());
        Assert.True(actual.GetProperty("isLocal").GetBoolean());
        Assert.False(actual.GetProperty("readOnly").GetBoolean());
        Assert.False(actual.GetProperty("isCustom").GetBoolean());
    }

    [Fact]
    public async Task GetContainers_ExistingContainer_ListsIt()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/blobs/containers");

        // Assert
        Assert.Contains(actual.EnumerateArray(), c => c.GetProperty("name").GetString() == container.Name);
    }

    [Fact]
    public async Task GetEntries_Root_ListsFolderAndFile()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/blobs/containers/{container.Name}/entries");

        // Assert
        Assert.Equal(["2025", "readme.txt"], actual.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task GetEntries_FolderPrefix_ListsDirectChildren()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/blobs/containers/{container.Name}/entries?prefix=2025/");

        // Assert
        Assert.Equal(["q1", "notes.txt"], actual.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Search_TextInSubfolder_ReturnsBlobBelowFolder()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/blobs/containers/{container.Name}/search?q=REPORT");

        // Assert
        var entry = Assert.Single(actual.GetProperty("entries").EnumerateArray());
        Assert.Equal("2025/q1/report.csv", entry.GetProperty("path").GetString());
    }

    [Fact]
    public async Task GetBlob_ExistingBlob_ReturnsAttachment()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.GetAsync(Blob(container.Name, "2025/q1/report.csv"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("report.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("a,b\n1,2\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetBlob_MissingBlob_ReturnsNotFound()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.GetAsync(Blob(container.Name, "nope.txt"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEntries_MissingContainer_ReturnsStorageError()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.GetAsync("/api/blobs/containers/no-such-container/entries");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Storage request failed", problem.GetProperty("title").GetString());
        Assert.StartsWith("ContainerNotFound", problem.GetProperty("detail").GetString());
    }

    // ---- deleting ----

    [Fact]
    public async Task DeleteBlob_ExistingBlob_ReturnsNoContentAndDeletesOnlyThatBlob()
    {
        // Arrange
        using var client = Start();
        var request = WithHeader(HttpMethod.Delete, Blob(container.Name, "readme.txt"));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await container.Exists("readme.txt"));
        Assert.True(await container.Exists("2025/notes.txt"));
    }

    [Fact]
    public async Task DeleteBlob_BlobAlreadyDeleted_ReturnsNotFound()
    {
        // Arrange
        using var client = Start();
        await container.Client.GetBlobClient("readme.txt").DeleteAsync();
        var request = WithHeader(HttpMethod.Delete, Blob(container.Name, "readme.txt"));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBlob_WithoutExplorerHeader_DeletesNothing()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.DeleteAsync(Blob(container.Name, "readme.txt"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await container.Exists("readme.txt"));
    }

    [Fact]
    public async Task DeleteBlob_FromAnotherOrigin_DeletesNothing()
    {
        // Arrange
        using var client = Start();
        var request = WithHeader(HttpMethod.Delete, Blob(container.Name, "readme.txt"));
        request.Headers.Add("Origin", "http://evil.example");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await container.Exists("readme.txt"));
    }

    [Fact]
    public async Task DeleteBlob_LockedExplorer_ReturnsForbiddenAndDeletesNothing()
    {
        // Arrange
        using var client = Start(readOnly: true);
        var request = WithHeader(HttpMethod.Delete, Blob(container.Name, "readme.txt"));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await container.Exists("readme.txt"));
    }

    [Fact]
    public async Task GetBlob_LockedExplorer_StillDownloads()
    {
        // Arrange
        using var client = Start(readOnly: true);

        // Act
        var response = await client.GetAsync(Blob(container.Name, "readme.txt"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetEntries_LockedExplorer_StillLists()
    {
        // Arrange
        using var client = Start(readOnly: true);

        // Act
        var response = await client.GetAsync($"/api/blobs/containers/{container.Name}/entries");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The emulator by name and random port, as after "Change connection": download and delete used to answer 404.

    [Fact]
    public async Task GetBlob_EmulatorReachedByName_ReturnsBlob()
    {
        // Arrange
        using var client = Start(azurite.HostnameConnectionString);

        // Act
        var response = await client.GetAsync(Blob(container.Name, "2025/q1/report.csv"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("a,b\n1,2\n", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DeleteBlob_EmulatorReachedByName_DeletesBlob()
    {
        // Arrange
        using var client = Start(azurite.HostnameConnectionString);
        var request = WithHeader(HttpMethod.Delete, Blob(container.Name, "2025/q1/report.csv"));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await container.Exists("2025/q1/report.csv"));
        Assert.True(await container.Exists("2025/notes.txt"));
    }

    // ---- changing the connection ----

    [Fact]
    public async Task PutConnection_OtherRouteToSameEmulator_MakesConnectionCustom()
    {
        // Arrange
        using var client = Start();
        // The same emulator by another route, so the change is visible in the info.
        var request = WithHeader(HttpMethod.Put, "/api/connection", Connection(azurite.HostnameConnectionString));

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await client.GetFromJsonAsync<JsonElement>("/api/connection");
        Assert.True(info.GetProperty("isCustom").GetBoolean());
    }

    [Fact]
    public async Task DeleteConnection_AfterPutConnection_RestoresDefaultConnection()
    {
        // Arrange
        using var client = Start();
        await client.SendAsync(WithHeader(HttpMethod.Put, "/api/connection", Connection(azurite.HostnameConnectionString)));
        var request = WithHeader(HttpMethod.Delete, "/api/connection");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await client.GetFromJsonAsync<JsonElement>("/api/connection");
        Assert.False(info.GetProperty("isCustom").GetBoolean());
    }

    [Fact]
    public async Task PutConnection_UnreachableEndpoint_ReturnsBadGatewayAndKeepsActiveConnection()
    {
        // Arrange
        using var client = Start();
        // Nothing listens on port 1.
        var unreachable = AzuriteFixture.BuildConnectionString("127.0.0.1", 1);
        var request = WithHeader(HttpMethod.Put, "/api/connection", Connection(unreachable));

        // Act
        var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Check that the service is running", problem.GetProperty("detail").GetString());
        var containers = await client.GetAsync("/api/blobs/containers");
        Assert.Equal(HttpStatusCode.OK, containers.StatusCode);
    }

    [Fact]
    public async Task PutConnection_WrongKey_ReturnsForbiddenWithoutEchoingKey()
    {
        // Arrange
        using var client = Start();
        var wrongKey = azurite.ConnectionString.Replace("Eby8vdM02xNOcqFlqUwJ", "WRONGKEYWRONGKEYWRON", StringComparison.Ordinal);
        var request = WithHeader(HttpMethod.Put, "/api/connection", Connection(wrongKey));

        // Act
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // Azurite says AuthorizationFailure and Azure says AuthenticationFailed for the same thing.
        Assert.Matches("Authentication|Authorization", body);
        Assert.DoesNotContain("WRONGKEYWRONGKEYWRON", body);
        var info = await client.GetFromJsonAsync<JsonElement>("/api/connection");
        Assert.False(info.GetProperty("isCustom").GetBoolean());
    }

    private HttpClient Start(string? connectionString = null, bool? readOnly = null)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("StorageExplorer:ConnectionString", connectionString ?? azurite.ConnectionString);

            // Azurite is reached from this process. If the tests ran inside a container, the app would rewrite
            // localhost and point at the host machine instead.
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IContainerEnvironment>();
                services.AddSingleton<IContainerEnvironment, NotInContainer>();
            });

            if (readOnly is { } value)
                builder.UseSetting("StorageExplorer:ReadOnly", value ? "true" : "false");
        });
        var client = factory.CreateClient();

        disposables.Add(client);
        disposables.Add(factory);

        return client;
    }

    private static HttpRequestMessage WithHeader(HttpMethod method, string uri, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Add("X-Storage-Explorer", "1");

        return request;
    }

    private static string Blob(string container, string path) =>
        $"/api/blobs/containers/{container}/blob?path={Uri.EscapeDataString(path)}";

    private static HttpContent Connection(string connectionString, bool allowWrites = false) =>
        JsonContent.Create(new { connectionString, allowWrites });
}

file sealed class NotInContainer : IContainerEnvironment
{
    public bool RunningInContainer => false;
}
