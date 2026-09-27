using Azure;
using Microsoft.AspNetCore.Diagnostics;

namespace StorageExplorer.Web;

/// <summary>Turns storage failures (missing container, bad request, ...) into problem details responses.</summary>
internal sealed class StorageExceptionHandler(IProblemDetailsService problemDetailsService, IContainerEnvironment container) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // When every retry fails the SDK throws an AggregateException that wraps the RequestFailedException.
        var storageException = exception switch
        {
            RequestFailedException failed => failed,
            AggregateException aggregate => aggregate.InnerExceptions.OfType<RequestFailedException>().FirstOrDefault(),
            _ => null,
        };

        if (storageException is null)
            return false;

        var status = storageException.Status is >= 400 and < 600
            ? storageException.Status
            : StatusCodes.Status502BadGateway;

        httpContext.Response.StatusCode = status;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = "Storage request failed",
                Detail = Describe(storageException),
            },
        });
    }

    private string Describe(RequestFailedException exception)
    {
        var message = CleanMessage(exception.Message);

        if (exception.ErrorCode is { Length: > 0 } code)
            return $"{code}: {message}";

        // Status 0 means there was no HTTP response at all: the endpoint could not be reached.
        if (exception.Status != 0)
            return message;

        return container.RunningInContainer
            ? $"{message}. Check that the service is running and listening on that port; from this container, " +
              $"localhost and 127.0.0.1 are reached through {LoopbackHostRewriter.HostGateway}."
            : $"{message}. Check that the service is running and listening on that port.";
    }

    /// <summary>
    /// <see cref="RequestFailedException.Message"/> is the service's own error text (occasionally with a RequestId
    /// and a timestamp folded in, which is normal for these APIs) followed by a block the SDK appends itself: the
    /// status, the error code again, and the raw response content and headers. That block starts at a line reading
    /// exactly <c>Status: </c>, is redundant with what <see cref="Describe"/> already reports, and is dropped.
    /// </summary>
    private static string CleanMessage(string message)
    {
        var index = message.IndexOf("\nStatus: ", StringComparison.Ordinal);

        return index < 0 ? message : message[..index].TrimEnd();
    }
}
