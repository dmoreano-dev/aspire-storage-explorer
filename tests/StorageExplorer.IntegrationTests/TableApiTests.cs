using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>
/// The table routes over HTTP with nothing replaced: real routes, real connection, real Azurite. Complements
/// <see cref="TableExplorerServiceTests"/> (the service on its own) by proving the route, the DI wiring and the JSON
/// shape (mixed property types included) all agree.
/// </summary>
[Collection(AzuriteCollection.Name)]
public sealed class TableApiTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestTable table = null!;
    private readonly List<IDisposable> disposables = [];

    public async Task InitializeAsync()
    {
        table = await TestTable.CreateAsync(azurite);
        await table.AddAsync(new TableEntity("a", "1")
        {
            { "Name", "Widget" },
            { "Price", 19.99 },
            { "InStock", true },
        });
    }

    public async Task DisposeAsync()
    {
        foreach (var disposable in disposables)
            disposable.Dispose();

        await table.DisposeAsync();
    }

    [Fact]
    public async Task GetTables_ExistingTable_ListsIt()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/tables");

        // Assert
        Assert.Contains(actual.EnumerateArray(), t => t.GetProperty("name").GetString() == table.Name);
    }

    [Fact]
    public async Task GetEntities_ExistingTable_ReturnsColumnsAndTheEntityWithItsPropertyTypesPreserved()
    {
        // Arrange
        using var client = Start();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>($"/api/tables/{table.Name}/entities");

        // Assert
        Assert.Equal(
            ["PartitionKey", "RowKey", "InStock", "Name", "Price", "Timestamp"],
            actual.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));

        var entity = Assert.Single(actual.GetProperty("entities").EnumerateArray());
        Assert.Equal("Widget", entity.GetProperty("Name").GetString());
        Assert.Equal(19.99, entity.GetProperty("Price").GetDouble());
        Assert.True(entity.GetProperty("InStock").GetBoolean());
        Assert.Equal(JsonValueKind.Null, actual.GetProperty("continuationToken").ValueKind);
    }

    [Fact]
    public async Task GetEntities_MissingTable_ReturnsStorageError()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.GetAsync("/api/tables/no-such-table/entities");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        // Azurite answers a query against a table that does not exist with a plain 400 (real Azure answers 404
        // TableNotFound instead); either way it flows through the same generic storage-error handling.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Storage request failed", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task CreateEntity_MixedPropertyTypes_ReturnsNoContentAndTheEntityCanBeQueriedBackWithItsTypesPreserved()
    {
        // Arrange
        using var client = Start();
        var request = WithHeader(HttpMethod.Post, $"/api/tables/{table.Name}/entities");
        request.Content = JsonContent.Create(new
        {
            partitionKey = "b",
            rowKey = "2",
            properties = new object[]
            {
                new { name = "Count", type = "Number", value = "42" },
                new { name = "Ratio", type = "Number", value = "3.5" },
                new { name = "Active", type = "Boolean", value = "true" },
                new { name = "Due", type = "DateTime", value = "2024-01-01T12:00:00Z" },
                new { name = "Id", type = "Guid", value = "11111111-2222-3333-4444-555555555555" },
            },
        });

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entities = await client.GetFromJsonAsync<JsonElement>($"/api/tables/{table.Name}/entities");
        var entity = entities.GetProperty("entities").EnumerateArray().Single(e => e.GetProperty("RowKey").GetString() == "2");
        Assert.Equal(42, entity.GetProperty("Count").GetInt64());
        Assert.Equal(3.5, entity.GetProperty("Ratio").GetDouble());
        Assert.True(entity.GetProperty("Active").GetBoolean());
        Assert.Equal("11111111-2222-3333-4444-555555555555", entity.GetProperty("Id").GetString());
    }

    [Fact]
    public async Task CreateEntity_WithoutExplorerHeader_CreatesNothing()
    {
        // Arrange
        using var client = Start();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/tables/{table.Name}/entities")
        {
            Content = JsonContent.Create(new { partitionKey = "b", rowKey = "2" }),
        };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var entities = await client.GetFromJsonAsync<JsonElement>($"/api/tables/{table.Name}/entities");
        Assert.Single(entities.GetProperty("entities").EnumerateArray());
    }

    [Fact]
    public async Task DeleteTable_ExistingTable_ReturnsNoContentAndRemovesIt()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.SendAsync(WithHeader(HttpMethod.Delete, $"/api/tables/{table.Name}"));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var listing = await client.GetFromJsonAsync<JsonElement>("/api/tables");
        Assert.DoesNotContain(listing.EnumerateArray(), t => t.GetProperty("name").GetString() == table.Name);
    }

    [Fact]
    public async Task DeleteEntity_ExistingEntity_ReturnsNoContentAndRemovesOnlyThatOne()
    {
        // Arrange
        await table.AddAsync(new TableEntity("b", "2"));
        using var client = Start();

        // Act
        var response = await client.SendAsync(WithHeader(HttpMethod.Delete, $"/api/tables/{table.Name}/entities?partitionKey=a&rowKey=1"));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var entities = await client.GetFromJsonAsync<JsonElement>($"/api/tables/{table.Name}/entities");
        var remaining = Assert.Single(entities.GetProperty("entities").EnumerateArray());
        Assert.Equal("b", remaining.GetProperty("PartitionKey").GetString());
    }

    [Fact]
    public async Task DeleteEntity_WithoutExplorerHeader_DeletesNothing()
    {
        // Arrange
        using var client = Start();

        // Act
        var response = await client.DeleteAsync($"/api/tables/{table.Name}/entities?partitionKey=a&rowKey=1");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var entities = await client.GetFromJsonAsync<JsonElement>($"/api/tables/{table.Name}/entities");
        Assert.Single(entities.GetProperty("entities").EnumerateArray());
    }

    [Fact]
    public async Task GetTables_ConnectionWithNoTableEndpoint_ReturnsBadRequest()
    {
        // Arrange
        // A blob container SAS: no AccountName, so a table client cannot be built from it at all. This never touches
        // the network (building the connection only parses the string), so it does not need Azurite to be reachable.
        const string blobSasOnly =
            "BlobEndpoint=https://acct.blob.example.invalid/;" +
            "SharedAccessSignature=sv=2021-08-06&ss=b&srt=sco&sp=r&se=2100-01-01&st=2020-01-01&spr=https&sig=abc";
        using var client = Start(blobSasOnly);

        // Act
        var response = await client.GetAsync("/api/tables");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", problem.GetProperty("title").GetString());
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
