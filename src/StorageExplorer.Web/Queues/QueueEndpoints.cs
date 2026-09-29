using StorageExplorer.Web.Endpoints;

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

        api.MapPost("/{queue}", async (
                string queue,
                IStorageConnection connection,
                IQueueExplorerService explorer,
                CancellationToken cancellationToken) =>
                {
                    if (connection.Queue is null) return NoQueueEndpoint();
                    await explorer.CreateQueueAsync(queue, cancellationToken);
                    return Results.NoContent();
                })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        api.MapGet("/{queue}/messages", async (
            string queue,
            IStorageConnection connection,
            IQueueExplorerService explorer,
            CancellationToken cancellationToken) =>
            connection.Queue is null
                ? NoQueueEndpoint()
                : Results.Ok(await explorer.PeekMessagesAsync(queue, cancellationToken)));

        api.MapDelete("/{queue}", async (
                string queue,
                IStorageConnection connection,
                IQueueExplorerService explorer,
                CancellationToken cancellationToken) =>
                connection.Queue is null
                    ? NoQueueEndpoint()
                    : await explorer.DeleteQueueAsync(queue, cancellationToken) ? Results.NoContent() : Results.NotFound())
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        api.MapDelete("/{queue}/messages", async (
                string queue,
                IStorageConnection connection,
                IQueueExplorerService explorer,
                CancellationToken cancellationToken) =>
                {
                    if (connection.Queue is null) return NoQueueEndpoint();
                    await explorer.ClearQueueAsync(queue, cancellationToken);
                    return Results.NoContent();
                })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        api.MapDelete("/{queue}/messages/peeked", async (
                string queue,
                IStorageConnection connection,
                IQueueExplorerService explorer,
                CancellationToken cancellationToken) =>
                {
                    if (connection.Queue is null) return NoQueueEndpoint();
                    var deleted = await explorer.DeletePeekedMessagesAsync(queue, cancellationToken);
                    return Results.Ok(new { deleted });
                })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        return app;
    }

    private static IResult NoQueueEndpoint() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "No queue endpoint",
            detail: "This connection has no queue endpoint (for example, a connection string scoped to blobs only). " +
                    "Change connection to one that has a QueueEndpoint or an AccountName.");
}
