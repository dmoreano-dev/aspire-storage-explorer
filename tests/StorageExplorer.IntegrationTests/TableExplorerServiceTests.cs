using Azure;
using Azure.Data.Tables;
using StorageExplorer.Web.Tables;

namespace StorageExplorer.Web.IntegrationTests;

/// <summary>The service against a real Azurite: what the emulator does is what the tests believe.</summary>
[Collection(AzuriteCollection.Name)]
public sealed class TableExplorerServiceTests(AzuriteFixture azurite) : IAsyncLifetime
{
    private TestTable table = null!;
    private TableExplorerService service = null!;

    public async Task InitializeAsync()
    {
        table = await TestTable.CreateAsync(azurite);
        service = table.Service();
    }

    public async Task DisposeAsync() => await table.DisposeAsync();

    // ---- tables ----

    [Fact]
    public async Task ListTablesAsync_ExistingTable_ReturnsIt()
    {
        // Act
        var actual = await service.ListTablesAsync(default);

        // Assert
        Assert.Contains(actual, t => t.Name == table.Name);
    }

    // ---- entities ----

    [Fact]
    public async Task QueryEntitiesAsync_EmptyTable_ReturnsNoEntities()
    {
        // Act
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);

        // Assert
        Assert.Empty(actual.Entities);
        Assert.Null(actual.ContinuationToken);
    }

    [Fact]
    public async Task QueryEntitiesAsync_EntitiesWithDifferentProperties_ColumnsAreTheUnionInDisplayOrder()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1") { { "Name", "Widget" }, { "Price", 19.99 } });
        await table.AddAsync(new TableEntity("a", "2") { { "OnlyOnThisOne", "x" } });

        // Act
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);

        // Assert
        // PartitionKey and RowKey first, then every other key found (alphabetically), then Timestamp last.
        Assert.Equal(["PartitionKey", "RowKey", "Name", "OnlyOnThisOne", "Price", "Timestamp"], actual.Columns);
    }

    [Fact]
    public async Task QueryEntitiesAsync_Entity_HasPropertiesButNotODataEtag()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1") { { "Name", "Widget" }, { "Price", 19.99 }, { "InStock", true } });

        // Act
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);

        // Assert
        var entity = Assert.Single(actual.Entities);
        Assert.Equal("a", entity["PartitionKey"]);
        Assert.Equal("1", entity["RowKey"]);
        Assert.Equal("Widget", entity["Name"]);
        Assert.Equal(19.99, entity["Price"]);
        Assert.Equal(true, entity["InStock"]);
        Assert.NotNull(entity["Timestamp"]);
        Assert.DoesNotContain("odata.etag", entity.Keys);
    }

    [Fact]
    public async Task QueryEntitiesAsync_MissingPropertyOnSomeEntities_RowIsMissingThatKeyInstead()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1") { { "Name", "Widget" } });
        await table.AddAsync(new TableEntity("a", "2"));

        // Act
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);

        // Assert
        var withName = actual.Entities.Single(e => e["RowKey"].Equals("1"));
        var withoutName = actual.Entities.Single(e => e["RowKey"].Equals("2"));
        Assert.True(withName.ContainsKey("Name"));
        Assert.False(withoutName.ContainsKey("Name"));
    }

    [Fact]
    public async Task QueryEntitiesAsync_Filter_ReturnsOnlyMatchingEntities()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1"));
        await table.AddAsync(new TableEntity("b", "2"));

        // Act
        var actual = await service.QueryEntitiesAsync(table.Name, "PartitionKey eq 'a'", null, default);

        // Assert
        var entity = Assert.Single(actual.Entities);
        Assert.Equal("a", entity["PartitionKey"]);
    }

    [Fact]
    public async Task QueryEntitiesAsync_InvalidFilter_ThrowsRequestFailedException()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.QueryEntitiesAsync(table.Name, "not odata at all!!!", null, default));

        // Assert
        Assert.Equal(400, actual.Status);
    }

    [Fact]
    public async Task QueryEntitiesAsync_MissingTable_ThrowsRequestFailedException()
    {
        // Act
        // Azurite answers a query against a table that does not exist with a plain 400 (real Azure answers 404
        // TableNotFound instead), but either way this is a storage error the generic exception handler already covers.
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.QueryEntitiesAsync("no-such-table", null, null, default));

        // Assert
        Assert.Equal(400, actual.Status);
    }

    [Fact]
    public async Task QueryEntitiesAsync_MoreThanOnePage_IsTruncatedWithContinuationToken()
    {
        // Arrange
        var table2 = await Many();
        var service2 = table2.Service();

        // Act
        var actual = await service2.QueryEntitiesAsync(table2.Name, null, null, default);

        // Assert
        Assert.Equal(TableExplorerService.PageSize, actual.Entities.Count);
        Assert.NotNull(actual.ContinuationToken);
    }

    [Fact]
    public async Task QueryEntitiesAsync_ContinuationTokenFromAPreviousPage_ReturnsTheRestWithNoTokenLeft()
    {
        // Arrange
        var table2 = await Many();
        var service2 = table2.Service();
        var first = await service2.QueryEntitiesAsync(table2.Name, null, null, default);

        // Act
        var actual = await service2.QueryEntitiesAsync(table2.Name, null, first.ContinuationToken, default);

        // Assert
        Assert.Equal(EntityCount - TableExplorerService.PageSize, actual.Entities.Count);
        Assert.Null(actual.ContinuationToken);
        Assert.DoesNotContain(actual.Entities, entity => first.Entities.Any(f => Equals(f["RowKey"], entity["RowKey"])));
    }

    // ---- delete table ----

    [Fact]
    public async Task DeleteTableAsync_ExistingTable_DeletesIt()
    {
        // Act
        await service.DeleteTableAsync(table.Name, default);

        // Assert
        Assert.DoesNotContain(await service.ListTablesAsync(default), t => t.Name == table.Name);
    }

    [Fact]
    public async Task DeleteTableAsync_MissingTable_ThrowsRequestFailedException()
    {
        // Act
        // Azurite answers with a plain 400 and no error code (real Azure answers 404 TableNotFound instead), the same
        // quirk QueryEntitiesAsync_MissingTable_ThrowsRequestFailedException already documents for this table.
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.DeleteTableAsync("no-such-table", default));

        // Assert
        Assert.Equal(400, actual.Status);
    }

    // ---- delete entity ----

    [Fact]
    public async Task DeleteEntityAsync_ExistingEntity_RemovesOnlyThatOne()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1") { { "Name", "Keep" } });
        await table.AddAsync(new TableEntity("a", "2") { { "Name", "Delete me" } });

        // Act
        await service.DeleteEntityAsync(table.Name, "a", "2", default);

        // Assert
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);
        var remaining = Assert.Single(actual.Entities);
        Assert.Equal("Keep", remaining["Name"]);
    }

    [Fact]
    public async Task DeleteEntityAsync_EmptyRowKey_DeletesIt()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", ""));

        // Act
        await service.DeleteEntityAsync(table.Name, "a", "", default);

        // Assert
        Assert.Empty((await service.QueryEntitiesAsync(table.Name, null, null, default)).Entities);
    }

    [Fact]
    public async Task DeleteEntityAsync_MissingEntity_CompletesWithoutThrowing()
    {
        // Act
        // Unlike deleting a table, the SDK does not surface a missing entity as an error here (confirmed against a
        // real Azurite): it treats the delete as already accomplished. Not throwing is the assertion.
        await service.DeleteEntityAsync(table.Name, "no-such-partition", "no-such-row", default);
    }

    // ---- create table ----

    [Fact]
    public async Task CreateTableAsync_NewName_CreatesTable()
    {
        // Arrange
        var name = $"t{Guid.NewGuid():N}";
        var client = new TableServiceClient(azurite.ConnectionString).GetTableClient(name);

        try
        {
            // Act
            await service.CreateTableAsync(name, default);

            // Assert
            Assert.Contains(await service.ListTablesAsync(default), t => t.Name == name);
        }
        finally
        {
            await client.DeleteAsync();
        }
    }

    [Fact]
    public async Task CreateTableAsync_ExistingTable_ThrowsTableAlreadyExists()
    {
        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.CreateTableAsync(table.Name, default));

        // Assert
        Assert.Equal(409, actual.Status);
        Assert.Equal("TableAlreadyExists", actual.ErrorCode);
    }

    // ---- create entity ----

    [Fact]
    public async Task CreateEntityAsync_MixedPropertyTypes_ReturnsThemWithTheirTypesPreserved()
    {
        // Arrange
        var properties = new Dictionary<string, object?>
        {
            ["Name"] = "Widget",
            ["Price"] = 19.99,
            ["Quantity"] = 5L,
            ["InStock"] = true,
            ["Due"] = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            ["Id"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };

        // Act
        await service.CreateEntityAsync(table.Name, "a", "1", properties, default);

        // Assert
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);
        var entity = Assert.Single(actual.Entities);
        Assert.Equal("Widget", entity["Name"]);
        Assert.Equal(19.99, entity["Price"]);
        Assert.Equal(5L, entity["Quantity"]);
        Assert.Equal(true, entity["InStock"]);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), entity["Id"]);
    }

    [Fact]
    public async Task CreateEntityAsync_NoProperties_CreatesTheEntityWithJustTheKeys()
    {
        // Act
        await service.CreateEntityAsync(table.Name, "a", "1", new Dictionary<string, object?>(), default);

        // Assert
        var actual = await service.QueryEntitiesAsync(table.Name, null, null, default);
        var entity = Assert.Single(actual.Entities);
        Assert.Equal("a", entity["PartitionKey"]);
        Assert.Equal("1", entity["RowKey"]);
    }

    [Fact]
    public async Task CreateEntityAsync_SamePartitionAndRowKeyAlreadyExists_ThrowsEntityAlreadyExists()
    {
        // Arrange
        await table.AddAsync(new TableEntity("a", "1"));

        // Act
        var actual = await Assert.ThrowsAsync<RequestFailedException>(() =>
            service.CreateEntityAsync(table.Name, "a", "1", new Dictionary<string, object?>(), default));

        // Assert
        Assert.Equal(409, actual.Status);
        Assert.Equal("EntityAlreadyExists", actual.ErrorCode);
    }

    // Filled once and only read: as many entities as one page plus a few, so a query needs two pages to see them all.
    private const int EntityCount = TableExplorerService.PageSize + 7;

    private Task<TestTable> Many() => azurite.SharedAsync("table-many", async () =>
    {
        var many = await TestTable.CreateAsync(azurite);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, EntityCount),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (index, _) => await many.AddAsync(new TableEntity("a", $"{index:D5}")));

        return many;
    });
}
