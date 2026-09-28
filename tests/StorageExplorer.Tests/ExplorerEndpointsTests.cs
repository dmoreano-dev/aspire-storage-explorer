using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using StorageExplorer.Web.Blobs;

namespace StorageExplorer.Web.Tests;

public class ExplorerEndpointsTests
{
    // ---- reading ----

    [Fact]
    public async Task GetConnection_RemoteAccount_ShowsAccountButNeverTheKey()
    {
        // Arrange
        using var host = new ApiHost(FakeStorageConnection.RemoteReadOnly);

        // Act
        var response = await host.Client.GetAsync("/api/connection");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = JsonDocument.Parse(body).RootElement;
        Assert.Equal("prodaccount", info.GetProperty("accountName").GetString());
        Assert.Equal("prodaccount.blob.example.invalid", info.GetProperty("endpoint").GetString());
        Assert.False(info.GetProperty("isLocal").GetBoolean());
        Assert.True(info.GetProperty("readOnly").GetBoolean());
        Assert.False(info.GetProperty("readOnlyLocked").GetBoolean());
        Assert.DoesNotContain("key", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetContainers_TwoContainers_ReturnsBothNames()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.Containers =
        [
            new ContainerSummary("photos", DateTimeOffset.Parse("2026-01-02T03:04:05Z")),
            new ContainerSummary("logs", null),
        ];

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers");
        var actual = await Json(response);

        // Assert
        Assert.Equal(["photos", "logs"], actual.EnumerateArray().Select(c => c.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task GetEntries_ContainerAndPrefix_ReturnsFoldersAndFiles()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.Listing = new EntryListing(
            [
                new ExplorerEntry("2025", "2025/", IsFolder: true, null, null, null),
                new ExplorerEntry("cat.png", "cat.png", IsFolder: false, 12, null, "image/png"),
            ],
            Truncated: true);

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/photos/entries?prefix=2025/");
        var actual = await Json(response);

        // Assert
        Assert.Equal(["entries:photos:2025/"], host.Explorer.Calls);
        Assert.True(actual.GetProperty("truncated").GetBoolean());
        var entries = actual.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.True(entries[0].GetProperty("isFolder").GetBoolean());
        Assert.Equal("image/png", entries[1].GetProperty("contentType").GetString());
    }

    [Fact]
    public async Task GetEntries_NoPrefix_ListsRoot()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        await host.Client.GetAsync("/api/blobs/containers/photos/entries");

        // Assert
        Assert.Equal(["entries:photos:"], host.Explorer.Calls);
    }

    // ---- search ----

    [Fact]
    public async Task Search_TextWithSpaces_PassesTrimmedTextToService()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/photos/search?prefix=2025/&q=%20report%20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["search:photos:2025/:report"], host.Explorer.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?q=")]
    [InlineData("?q=%20%20")]
    public async Task Search_MissingOrBlankText_ReturnsBadRequest(string query)
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync($"/api/blobs/containers/photos/search{query}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Missing search text", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Explorer.Calls);
    }

    // ---- download ----

    [Fact]
    public async Task GetBlob_ExistingBlob_ReturnsContentTypeAndFileName()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.Download = new BlobDownload(new MemoryStream([1, 2, 3]), "text/csv", "report 2025.csv");

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/reports/blob?path=2025/report%202025.csv");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("report 2025.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(["download:reports:2025/report 2025.csv"], host.Explorer.Calls);
    }

    [Fact]
    public async Task GetBlob_ExistingBlob_WithInlineTrue_OmitsContentDisposition()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.Download = new BlobDownload(new MemoryStream([1, 2, 3]), "image/png", "photo.png");

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/photos/blob?path=photo.png&inline=true");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Equal([1, 2, 3], await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task GetBlob_MissingBlob_ReturnsNotFound()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.Download = null;

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/reports/blob?path=nope.csv");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBlob_MissingPathParameter_ReturnsBadRequest()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/api/blobs/containers/reports/blob");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Explorer.Calls);
    }

    // ---- create container / folder ----

    [Fact]
    public async Task PostContainer_Name_CreatesAndReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/blobs/containers/newcontainer");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createContainer:newcontainer"], host.Explorer.Calls);
    }

    [Fact]
    public async Task PostFolder_Path_CreatesAndReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/blobs/containers/photos/folder?path=2025/new");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["createFolder:photos:2025/new"], host.Explorer.Calls);
    }

    // ---- upload ----

    [Fact]
    public async Task PostBlob_Content_UploadsWithContentTypeAndReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/blobs/containers/photos/blob?path=2025/cat.png");
        request.Content = new ByteArrayContent([1, 2, 3]);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["upload:photos:2025/cat.png:image/png"], host.Explorer.Calls);
    }

    [Fact]
    public async Task PostBlob_NoContentType_UploadsWithNullContentType()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Post, "/api/blobs/containers/photos/blob?path=notes.txt");
        request.Content = new ByteArrayContent([1]);
        request.Content.Headers.ContentType = null;

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["upload:photos:notes.txt:"], host.Explorer.Calls);
    }

    // ---- delete ----

    [Fact]
    public async Task DeleteBlob_ExistingBlob_ReturnsNoContent()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/blobs/containers/photos/blob?path=2025/cat.png");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(["delete:photos:2025/cat.png"], host.Explorer.Calls);
    }

    [Fact]
    public async Task DeleteBlob_MissingBlob_ReturnsNotFound()
    {
        // Arrange
        using var host = new ApiHost();
        host.Explorer.DeleteResult = false;
        var request = host.Request(HttpMethod.Delete, "/api/blobs/containers/photos/blob?path=nope.png");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- changing the connection ----

    [Fact]
    public async Task PutConnection_ValidConnectionString_UsesItAndReturnsInfo()
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, TestConnectionStrings.Remote);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (clients, allowWrites) = Assert.Single(host.Connection.Used);
        Assert.Equal("prodaccount", clients.Blob.AccountName);
        Assert.False(allowWrites);
        Assert.Equal("devstoreaccount1", (await Json(response)).GetProperty("accountName").GetString());
    }

    [Fact]
    public async Task PutConnection_AllowWrites_PassesItToConnection()
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, TestConnectionStrings.Remote, allowWrites: true);

        // Act
        await host.Client.SendAsync(request);

        // Assert
        Assert.True(Assert.Single(host.Connection.Used).AllowWrites);
    }

    [Fact]
    public async Task PutConnection_ConnectionStringWithWhitespace_TrimsIt()
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, $"  {TestConnectionStrings.Remote}\n");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("prodaccount", Assert.Single(host.Connection.Used).Clients.Blob.AccountName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a connection string")]
    [InlineData("AccountName=onlyaname")]
    [InlineData("https://prodaccount.blob.example.invalid")]
    public async Task PutConnection_InvalidConnectionString_ReturnsBadRequestAndKeepsConnection(string? value)
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, value);

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Invalid connection string", (await Json(response)).GetProperty("title").GetString());
        Assert.Empty(host.Connection.Used);
    }

    [Fact]
    public async Task PutConnection_ConnectionStringTooLong_ReturnsBadRequest()
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, TestConnectionStrings.Remote + new string('a', 4096));

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Connection.Used);
    }

    [Fact]
    public async Task PutConnection_RefusedConnectionString_DoesNotEchoIt()
    {
        // Arrange
        using var host = new ApiHost();
        var request = PutConnection(host, "AccountName=x;AccountKey=SUPER-SECRET-KEY");

        // Act
        var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.DoesNotContain("SUPER-SECRET-KEY", body);
    }

    [Fact]
    public async Task PutConnection_ConnectionThatFailsTheCheck_ReportsStorageErrorAndKeepsConnection()
    {
        // Arrange
        using var host = new ApiHost();
        host.Connection.UseFails = new RequestFailedException(403, "Server failed to authenticate the request.", "AuthenticationFailed", null);
        var request = PutConnection(host, TestConnectionStrings.Remote);

        // Act
        var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("AuthenticationFailed", body);
        Assert.DoesNotContain("c2VjcmV0LWtleS1kby1ub3QtbGVhaw", body);
        Assert.Empty(host.Connection.Used);
    }

    [Fact]
    public async Task PutConnection_InContainerWithLoopbackHost_UsesTheHostMachine()
    {
        // Arrange
        using var host = new ApiHost(runningInContainer: true);
        var request = PutConnection(host, TestConnectionStrings.Local);

        // Act
        await host.Client.SendAsync(request);

        // Assert
        var (clients, _) = Assert.Single(host.Connection.Used);
        Assert.Equal("host.docker.internal", clients.Blob.Uri.Host);
    }

    [Fact]
    public async Task PutConnection_OutsideContainerWithLoopbackHost_KeepsTheHost()
    {
        // Arrange
        using var host = new ApiHost(runningInContainer: false);
        var request = PutConnection(host, TestConnectionStrings.Local);

        // Act
        await host.Client.SendAsync(request);

        // Assert
        var (clients, _) = Assert.Single(host.Connection.Used);
        Assert.Equal("127.0.0.1", clients.Blob.Uri.Host);
    }

    [Fact]
    public async Task PutConnection_InContainerWithDevInternalHost_UsesThePlainContainerName()
    {
        // Arrange
        using var host = new ApiHost(runningInContainer: true);
        var request = PutConnection(host, TestConnectionStrings.DevInternal);

        // Act
        await host.Client.SendAsync(request);

        // Assert
        var (clients, _) = Assert.Single(host.Connection.Used);
        Assert.Equal("storage", clients.Blob.Uri.Host);
    }

    [Fact]
    public async Task PutConnection_OutsideContainerWithDevInternalHost_KeepsTheHost()
    {
        // Arrange
        using var host = new ApiHost(runningInContainer: false);
        var request = PutConnection(host, TestConnectionStrings.DevInternal);

        // Act
        await host.Client.SendAsync(request);

        // Assert
        var (clients, _) = Assert.Single(host.Connection.Used);
        Assert.Equal("storage.dev.internal", clients.Blob.Uri.Host);
    }

    [Fact]
    public async Task DeleteConnection_CustomConnection_ResetsToDefault()
    {
        // Arrange
        using var host = new ApiHost();
        var request = host.Request(HttpMethod.Delete, "/api/connection");

        // Act
        var response = await host.Client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Connection.ResetCount);
    }

    // ---- the page ----

    [Fact]
    public async Task GetRoot_Page_ReturnsHtmlThatLoadsNothingFromOtherSites()
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var references = ReferencesIn(html);
        Assert.NotEmpty(references);
        Assert.DoesNotContain(references, reference => reference.Contains("://") || reference.StartsWith("//", StringComparison.Ordinal));
    }

    // The local files the page points at, found in the real page when the tests are discovered.
    public static IEnumerable<object[]> LocalPageReferences
    {
        get
        {
            using var host = new ApiHost();
            var html = host.Client.GetStringAsync("/").GetAwaiter().GetResult();

            return ReferencesIn(html)
                .Where(reference => !reference.Contains("://") && !reference.StartsWith("//", StringComparison.Ordinal))
                .Select(reference => new object[] { reference })
                .ToList();
        }
    }

    [Theory]
    [MemberData(nameof(LocalPageReferences))]
    public async Task GetStaticFile_LocalReferenceOfThePage_IsServed(string reference)
    {
        // Arrange
        using var host = new ApiHost();

        // Act
        var response = await host.Client.GetAsync(reference);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static HttpRequestMessage PutConnection(ApiHost host, string? connectionString, bool? allowWrites = null)
    {
        var request = host.Request(HttpMethod.Put, "/api/connection");
        request.Content = JsonContent.Create(new { connectionString, allowWrites = allowWrites ?? false });

        return request;
    }

    // The page makes no requests to other sites, and every local file it points at has to exist.
    private static readonly Regex Reference =
        new("""(?:src|href)\s*=\s*"([^"#]+)"|(?:src|href)\s*=\s*'([^'#]+)'""");

    private static List<string> ReferencesIn(string html) =>
        Reference.Matches(html)
            .Select(match => match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value)
            .Where(reference => !reference.StartsWith("data:", StringComparison.Ordinal))
            .ToList();
}
