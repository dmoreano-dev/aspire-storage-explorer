using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Data.Tables;
using StorageExplorer.Web.Tables;

namespace StorageExplorer.Web.Tests;

public class TableEndpointsTests
{
    // ---- tables ----

    [Fact]
    public async Task GetTables_TwoTables_ReturnsBothNames()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        host.Tables.Tables = [new TableSummary("widgets"), new TableSummary("orders")];

        // Act
        var response = await host.Client.GetAsync("/api/tables");
        var actual = await Json(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["widgets", "orders"], actual.EnumerateArray().Select(t => t.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task GetTables_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/tables");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- create table ----

    [Fact]
    public async Task CreateTable_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createTable:widgets"], host.Tables.Calls);
    }

    [Fact]
    public async Task CreateTable_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- entities ----

    [Fact]
    public async Task GetEntities_Table_ReturnsColumnsAndRows()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        host.Tables.Page = new EntityPage(
            ["PartitionKey", "RowKey", "Name", "Timestamp"],
            [new Dictionary<string, object?> { ["PartitionKey"] = "a", ["RowKey"] = "1", ["Name"] = "Widget" }],
            "next-token");

        // Act
        var response = await host.Client.GetAsync("/api/tables/widgets/entities");
        var actual = await Json(response);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["entities:widgets::"], host.Tables.Calls);
        Assert.Equal(
            ["PartitionKey", "RowKey", "Name", "Timestamp"],
            actual.GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        var row = Assert.Single(actual.GetProperty("entities").EnumerateArray());
        Assert.Equal("Widget", row.GetProperty("Name").GetString());
        Assert.Equal("next-token", actual.GetProperty("continuationToken").GetString());
    }

    [Fact]
    public async Task GetEntities_FilterAndContinuationToken_PassesThemToTheService()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);

        // Act
        await host.Client.GetAsync("/api/tables/widgets/entities?filter=PartitionKey%20eq%20'a'&continuationToken=abc");

        // Assert
        Assert.Equal(["entities:widgets:PartitionKey eq 'a':abc"], host.Tables.Calls);
    }

    [Fact]
    public async Task GetEntities_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/tables/widgets/entities");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- create entity ----

    [Fact]
    public async Task CreateEntity_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new
        {
            partitionKey = "a",
            rowKey = "1",
            properties = new[] { new { name = "Name", type = "String", value = "Widget" } },
        });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createEntity:widgets:a:1:Name"], host.Tables.Calls);
    }

    [Fact]
    public async Task CreateEntity_NoProperties_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new { partitionKey = "a", rowKey = "1" });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createEntity:widgets:a:1:"], host.Tables.Calls);
    }

    [Fact]
    public async Task CreateEntity_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new { partitionKey = "a", rowKey = "1" });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("a", null)]
    [InlineData("", "1")]
    [InlineData("a", "")]
    public async Task CreateEntity_MissingKey_ReturnsBadRequestWithoutCallingTheService(string? partitionKey, string? rowKey)
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new { partitionKey, rowKey });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Missing keys", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    [Fact]
    public async Task CreateEntity_PropertyValueDoesNotMatchItsType_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new
        {
            partitionKey = "a",
            rowKey = "1",
            properties = new[] { new { name = "Price", type = "Number", value = "not a number" } },
        });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await Json(response);
        Assert.Equal("Invalid property", problem.GetProperty("title").GetString());
        Assert.Contains("Price", problem.GetProperty("detail").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    [Fact]
    public async Task CreateEntity_PropertyNamedLikeAReservedColumn_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Post, "/api/tables/widgets/entities");
        request.Content = JsonContent.Create(new
        {
            partitionKey = "a",
            rowKey = "1",
            properties = new[] { new { name = "Timestamp", type = "String", value = "x" } },
        });

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid property", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- delete table ----

    [Fact]
    public async Task DeleteTable_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Delete, "/api/tables/widgets");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["deleteTable:widgets"], host.Tables.Calls);
    }

    [Fact]
    public async Task DeleteTable_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/tables/widgets");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- delete entity ----

    [Fact]
    public async Task DeleteEntity_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Delete, "/api/tables/widgets/entities?partitionKey=a&rowKey=1");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["deleteEntity:widgets:a:1"], host.Tables.Calls);
    }

    [Fact]
    public async Task DeleteEntity_EmptyRowKey_PassesItThrough()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);
        var request = host.Request(HttpMethod.Delete, "/api/tables/widgets/entities?partitionKey=a&rowKey=");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["deleteEntity:widgets:a:"], host.Tables.Calls);
    }

    [Fact]
    public async Task DeleteEntity_NoTableEndpoint_ReturnsBadRequestWithoutCallingTheService()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/tables/widgets/entities?partitionKey=a&rowKey=1");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No table endpoint", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Tables.Calls);
    }

    // ---- these routes never change data, so the read-only guards do not apply to them ----

    [Fact]
    public async Task GetTables_LockedExplorer_StillLists()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        host.Connection.Table = new TableServiceClient(TestConnectionStrings.Local);

        // Act
        var response = await host.Client.GetAsync("/api/tables");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
