using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StorageExplorer.Web.Blobs;

namespace StorageExplorer.Web.Tests;

/// <summary>
/// The explorer has no login, so these checks are what stands between the network and the storage account.
/// </summary>
public class GuardTests
{
    private const string DeleteBlobUri = "/api/blobs/containers/photos/blob?path=cat.png";

    [Fact]
    public async Task DeleteBlob_WithoutExplorerHeader_ReturnsBadRequestAndDeletesNothing()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri, explorerHeader: false);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Storage-Explorer", (await Problem(response)).GetProperty("title").GetString());
        Assert.Empty(host.Explorer.Calls);
    }

    [Fact]
    public async Task DeleteBlob_HeaderAndNoOrigin_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["delete:photos:cat.png"], host.Explorer.Calls);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://LOCALHOST")]
    public async Task DeleteBlob_SameOrigin_ReturnsNoContent(string origin)
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri, origin: origin);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("http://localhost:9999")]
    [InlineData("http://localhost.evil.example")]
    [InlineData("null")]
    [InlineData("not a url")]
    public async Task DeleteBlob_AnotherOrigin_ReturnsForbidden(string origin)
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri, origin: origin);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Explorer.Calls);
    }

    [Fact]
    public async Task GetContainers_CrossOriginRequest_SendsNoCorsHeaders()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Get, "/api/blobs/containers", explorerHeader: false, origin: "http://evil.example");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Preflight_DeleteFromAnotherOrigin_IsNotGrantedPermission()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Options, DeleteBlobUri, explorerHeader: false, origin: "http://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "DELETE");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DeleteBlob_LockedExplorer_ReturnsForbiddenWithReason()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await Problem(response);
        Assert.Equal("The explorer is read-only", problem.GetProperty("title").GetString());
        Assert.Contains("readOnly is set in WithStorageExplorer", problem.GetProperty("detail").GetString());
        Assert.Empty(host.Explorer.Calls);
    }

    [Fact]
    public async Task DeleteBlob_RemoteReadOnlyAccount_ReturnsForbiddenWithHowToAllow()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.RemoteReadOnly);
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await Problem(response);
        Assert.Contains("Allow changes", problem.GetProperty("detail").GetString());
        Assert.Empty(host.Explorer.Calls);
    }

    [Fact]
    public async Task DeleteBlob_LockedExplorerWithoutHeader_ReturnsBadRequestBeforeReadOnlyCheck()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        var request = host.Request(HttpMethod.Delete, DeleteBlobUri, explorerHeader: false);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBlob_LockedExplorer_ReturnsOk()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        host.Explorer.Download = new BlobDownload(new MemoryStream([1, 2, 3]), "image/png", "cat.png");

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/photos/blob?path=cat.png");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The tests below take every route the app registered instead of naming them, so an endpoint added later that
    // changes data (delete a queue, clear a table, ...) is checked without anyone remembering to add a test for it.
    // Each route is a case of its own, found in the real app when the tests are discovered.

    public static IEnumerable<object[]> DataChangingEndpointRows =>
        Discovered.Value.Select(endpoint => new object[] { endpoint.Method, endpoint.Uri });

    // Changing the connection does not change data in the account, so it is not read-only.
    public static IEnumerable<object[]> AccountDataChangingEndpointRows =>
        Discovered.Value
            .Where(endpoint => !endpoint.Uri.StartsWith("/api/connection", StringComparison.Ordinal))
            .Select(endpoint => new object[] { endpoint.Method, endpoint.Uri });

    [Fact]
    public void DataChangingEndpoints_RegisteredRoutes_AreExactlyTheExpectedOnes()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var actual = DataChangingEndpoints(host).Select(e => $"{e.Method} {e.Uri[..e.Uri.IndexOf('?')]}").Order().ToList();

        // Assert
        // Adding an endpoint that changes data is a decision to review, so it has to be added here on purpose.
        Assert.Equal(
            [
                "DELETE /api/blobs/containers/x/blob",
                "DELETE /api/connection",
                "DELETE /api/queues/x",
                "DELETE /api/queues/x/messages",
                "DELETE /api/queues/x/messages/peeked",
                "DELETE /api/tables/x",
                "DELETE /api/tables/x/entities",
                "PUT /api/connection",
            ],
            actual);
    }

    [Theory]
    [MemberData(nameof(DataChangingEndpointRows))]
    public async Task DataChangingEndpoint_WithoutExplorerHeader_ReturnsBadRequest(string method, string uri)
    {
        // Arrange
        using var host = new ApiHost();
        var request = DataChangingRequest(host, method, uri, explorerHeader: false);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Storage-Explorer", (await Problem(response)).GetProperty("title").GetString());
        AssertNoExplorerCalls(host);
        Assert.Empty(host.Connection.Used);
        Assert.Equal(0, host.Connection.ResetCount);
    }

    [Theory]
    [MemberData(nameof(DataChangingEndpointRows))]
    public async Task DataChangingEndpoint_AnotherOrigin_ReturnsForbidden(string method, string uri)
    {
        // Arrange
        using var host = new ApiHost();
        var request = DataChangingRequest(host, method, uri, explorerHeader: true, origin: "http://evil.example");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        AssertNoExplorerCalls(host);
        Assert.Empty(host.Connection.Used);
        Assert.Equal(0, host.Connection.ResetCount);
    }

    [Theory]
    [MemberData(nameof(AccountDataChangingEndpointRows))]
    public async Task DataChangingEndpoint_LockedExplorer_ReturnsForbidden(string method, string uri)
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.Locked);
        var request = DataChangingRequest(host, method, uri, explorerHeader: true);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("The explorer is read-only", (await Problem(response)).GetProperty("title").GetString());
        AssertNoExplorerCalls(host);
    }

    [Theory]
    [MemberData(nameof(AccountDataChangingEndpointRows))]
    public async Task DataChangingEndpoint_RemoteReadOnlyAccount_ReturnsForbidden(string method, string uri)
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.RemoteReadOnly);
        var request = DataChangingRequest(host, method, uri, explorerHeader: true);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("The explorer is read-only", (await Problem(response)).GetProperty("title").GetString());
        AssertNoExplorerCalls(host);
    }

    // A route belongs to one of the three explorer fakes; checking only Explorer (Blob) would trivially pass for a
    // Queue or Table route without proving anything, since that fake would never be touched by it anyway.
    private static void AssertNoExplorerCalls(ApiHost host)
    {
        Assert.Empty(host.Explorer.Calls);
        Assert.Empty(host.Queues.Calls);
        Assert.Empty(host.Tables.Calls);
    }

    private static async Task<JsonElement> Problem(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static readonly Regex RouteParameter = new(@"\{[^}]+\}");

    private static readonly Lazy<List<(string Method, string Uri)>> Discovered = new(() =>
    {
        using var host = new ApiHost();

        return DataChangingEndpoints(host);
    });

    private static List<(string Method, string Uri)> DataChangingEndpoints(ApiHost host) =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [])
                .Where(method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
                .Select(method => (
                    Method: method,
                    Uri: RouteParameter.Replace(endpoint.RoutePattern.RawText!, "x") + "?path=x&q=x&prefix=x&partitionKey=x&rowKey=x")))
            .ToList();

    private static HttpRequestMessage DataChangingRequest(ApiHost host, string method, string uri, bool explorerHeader, string? origin = null)
    {
        var request = host.Request(new HttpMethod(method), uri, explorerHeader, origin);

        if (method != HttpMethod.Delete.Method)
            request.Content = JsonContent.Create(new { });

        return request;
    }
}
