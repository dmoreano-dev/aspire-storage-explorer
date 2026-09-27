namespace StorageExplorer.Web.Queues;

internal static class QueueEndpoints
{
    public static IEndpointRouteBuilder MapQueueEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/queues");

        api.MapGet("", async (
            IStorageConnection connection,
            IQueueExplorerService explorer,
            CancellationToken cancellationToken) =>
            connection.Queue is null
                ? NoQueueEndpoint()
                : Results.Ok(await explorer.ListQueuesAsync(cancellationToken)));

        api.MapGet("/{queue}/messages", async (
            string queue,
            IStorageConnection connection,
            IQueueExplorerService explorer,
            CancellationToken cancellationToken) =>
            connection.Queue is null
                ? NoQueueEndpoint()
                : Results.Ok(await explorer.PeekMessagesAsync(queue, cancellationToken)));

        return app;
    }

    private static IResult NoQueueEndpoint() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "No queue endpoint",
            detail: "This connection has no queue endpoint (for example, a connection string scoped to blobs only). " +
                    "Change connection to one that has a QueueEndpoint or an AccountName.");
}
