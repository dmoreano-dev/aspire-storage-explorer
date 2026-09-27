namespace StorageExplorer.Web.IntegrationTests;

[Collection(AzuriteCollection.Name)]
public class AzuriteSmokeTests(AzuriteFixture azurite)
{
    [Fact]
    public async Task ListContainersAsync_NewContainer_ListsIt()
    {
        // Arrange
        await using var container = await TestContainer.CreateAsync(azurite);
        var service = container.Service();

        // Act
        var actual = await service.ListContainersAsync(default);

        // Assert
        Assert.Contains(actual, c => c.Name == container.Name);
    }
}
