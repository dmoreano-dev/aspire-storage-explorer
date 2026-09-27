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

        return app;
    }

    private static IResult NoTableEndpoint() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "No table endpoint",
            detail: "This connection has no table endpoint (for example, a connection string scoped to blobs only). " +
                    "Change connection to one that has a TableEndpoint or an AccountName.");
}
