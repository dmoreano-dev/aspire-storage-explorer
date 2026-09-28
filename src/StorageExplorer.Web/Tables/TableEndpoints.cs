using StorageExplorer.Web.Endpoints;

namespace StorageExplorer.Web.Tables;

internal static class TableEndpoints
{
    public static IEndpointRouteBuilder MapTableEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/tables");

        api.MapGet("", async (
            IStorageConnection connection,
            ITableExplorerService explorer,
            CancellationToken cancellationToken) =>
            connection.Table is null
                ? NoTableEndpoint()
                : Results.Ok(await explorer.ListTablesAsync(cancellationToken)));

        api.MapGet("/{table}/entities", async (
            string table,
            string? filter,
            string? continuationToken,
            IStorageConnection connection,
            ITableExplorerService explorer,
            CancellationToken cancellationToken) =>
            connection.Table is null
                ? NoTableEndpoint()
                : Results.Ok(await explorer.QueryEntitiesAsync(table, filter, continuationToken, cancellationToken)));

        api.MapDelete("/{table}", async (
                string table,
                IStorageConnection connection,
                ITableExplorerService explorer,
                CancellationToken cancellationToken) =>
                {
                    if (connection.Table is null) return NoTableEndpoint();
                    await explorer.DeleteTableAsync(table, cancellationToken);
                    return Results.NoContent();
                })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        api.MapDelete("/{table}/entities", async (
                string table,
                string partitionKey,
                string rowKey,
                IStorageConnection connection,
                ITableExplorerService explorer,
                CancellationToken cancellationToken) =>
                {
                    if (connection.Table is null) return NoTableEndpoint();
                    await explorer.DeleteEntityAsync(table, partitionKey, rowKey, cancellationToken);
                    return Results.NoContent();
                })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        return app;
    }

    private static IResult NoTableEndpoint() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "No table endpoint",
            detail: "This connection has no table endpoint (for example, a connection string scoped to blobs only). " +
                    "Change connection to one that has a TableEndpoint or an AccountName.");
}
