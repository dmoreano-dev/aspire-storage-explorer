namespace StorageExplorer.Web.Tests;

public class LocalEndpointTests
{
    [Theory]
    [InlineData("http://localhost:10000/devstoreaccount1")]
    [InlineData("http://LOCALHOST:10000/devstoreaccount1")]
    [InlineData("http://127.0.0.1:10000/devstoreaccount1")]
    [InlineData("http://127.0.0.42:10000/devstoreaccount1")]
    [InlineData("http://[::1]:10000/devstoreaccount1")]
    [InlineData("http://host.docker.internal:10000/devstoreaccount1")]
    [InlineData("http://HOST.DOCKER.INTERNAL:10000/devstoreaccount1")]
    [InlineData("http://storage:10000/devstoreaccount1")]
    [InlineData("http://azurite.localhost:10000/devstoreaccount1")]
    public void IsLocal_EndpointOnThisMachine_ReturnsTrue(string endpoint)
    {
        // Arrange
        var uri = new Uri(endpoint);

        // Act
        var actual = LocalEndpoint.IsLocal(uri);

        // Assert
        Assert.True(actual);
    }

    [Theory]
    [InlineData("https://myaccount.blob.core.windows.net")]
    [InlineData("https://myaccount.blob.core.chinacloudapi.cn")]
    [InlineData("http://10.0.0.5:10000/devstoreaccount1")]
    [InlineData("http://192.168.1.10:10000/devstoreaccount1")]
    [InlineData("http://[2001:db8::1]:10000/devstoreaccount1")]
    [InlineData("http://storage.dev.internal:10000/devstoreaccount1")]
    [InlineData("http://myhost.example.com:10000/devstoreaccount1")]
    // A name that only starts or ends like a local one must not pass for it.
    [InlineData("http://localhost.evil.com/devstoreaccount1")]
    [InlineData("http://host.docker.internal.evil.com/devstoreaccount1")]
    [InlineData("http://evil-localhost.com/devstoreaccount1")]
    public void IsLocal_EndpointNotOnThisMachine_ReturnsFalse(string endpoint)
    {
        // Arrange
        var uri = new Uri(endpoint);

        // Act
        var actual = LocalEndpoint.IsLocal(uri);

        // Assert
        Assert.False(actual);
    }
}
