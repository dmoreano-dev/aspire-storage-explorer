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
