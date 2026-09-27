using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;

namespace StorageExplorer.Web.Tests;

public class StorageExceptionHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_StorageError_KeepsStatusAndCode()
    {
        // Arrange
        var failure = new RequestFailedException(404, "The specified container does not exist.", "ContainerNotFound", null);

        // Act
        var (status, problem) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("Storage request failed", problem.GetProperty("title").GetString());
        Assert.Equal("ContainerNotFound: The specified container does not exist.", problem.GetProperty("detail").GetString());
        Assert.Equal(404, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task TryHandleAsync_StorageErrorWithoutCode_ShowsMessage()
    {
        // Arrange
        var failure = new RequestFailedException(400, "Bad request.");

        // Act
        var (status, problem) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("Bad request.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_AggregateOfStorageError_UnwrapsIt()
    {
        // Arrange
        var failure = new AggregateException(
            new InvalidOperationException("retry 1"),
            new RequestFailedException(503, "The server is busy.", "ServerBusy", null));

        // Act
        var (status, problem) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.StartsWith("ServerBusy", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_StatusZero_ReturnsBadGatewayWithHint()
    {
        // Arrange
        var failure = new RequestFailedException(0, "Connection refused");

        // Act
        var (status, problem) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, status);
        Assert.Contains("Check that the service is running", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task TryHandleAsync_StatusZeroInContainer_HintSaysHowLoopbackIsReached()
    {
        // Arrange
        var failure = new RequestFailedException(0, "Connection refused");

        // Act
        var (_, problem) = await RequestContainersFailingWith(failure, runningInContainer: true);

        // Assert
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("Check that the service is running", detail);
        Assert.Contains("host.docker.internal", detail);
    }

    [Fact]
    public async Task TryHandleAsync_StatusZeroOutsideContainer_HintDoesNotMentionTheHostMachineName()
    {
        // Arrange
        var failure = new RequestFailedException(0, "Connection refused");

        // Act
        var (_, problem) = await RequestContainersFailingWith(failure, runningInContainer: false);

        // Assert
        var detail = problem.GetProperty("detail").GetString();
        Assert.Contains("Check that the service is running", detail);
        Assert.DoesNotContain("host.docker.internal", detail);
    }

    [Fact]
    public async Task TryHandleAsync_NonErrorStatus_ReturnsBadGateway()
    {
        // Arrange
        var failure = new RequestFailedException(304, "Not modified.", "ConditionNotMet", null);

        // Act
        var (status, _) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, status);
    }

    [Fact]
    public async Task TryHandleAsync_NonStorageError_IsNotDescribedAsStorageError()
    {
        // Arrange
        var failure = new InvalidOperationException("boom with internal details");

        // Act
        var (status, problem) = await RequestContainersFailingWith(failure);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.NotEqual("Storage request failed", problem.GetProperty("title").GetString());
        Assert.DoesNotContain("boom with internal details", problem.ToString());
    }

    /// <summary>Requests the containers from an app whose storage service fails with the given exception.</summary>
    private static async Task<(HttpStatusCode Status, JsonElement Problem)> RequestContainersFailingWith(
        Exception failure,
        bool runningInContainer = false)
    {
        using var host = new ApiHost(runningInContainer: runningInContainer);
        host.Explorer.Fails = failure;

        var response = await host.Client.GetAsync("/api/containers");

        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }
}
