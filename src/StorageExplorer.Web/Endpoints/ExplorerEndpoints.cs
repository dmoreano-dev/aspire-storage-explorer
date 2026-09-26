namespace StorageExplorer.Web.Endpoints;

internal static class ExplorerEndpoints
{
    // A storage account connection string is a few hundred characters; this only stops absurd payloads.
    private const int MaxConnectionStringLength = 4096;

    public static IEndpointRouteBuilder MapExplorerEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/connection", (IStorageConnection connection) => Results.Ok(connection.Info));

        // Changing the account is as sensitive as deleting, so it has the same guard. The connection string is
        // never echoed back, logged or included in an error.
        api.MapPut("/connection", async (
                SetConnectionRequest request,
                IStorageConnection connection,
                CancellationToken cancellationToken) =>
            {
                var value = request.ConnectionString?.Trim();

                if (string.IsNullOrEmpty(value) || value.Length > MaxConnectionStringLength)
                    return InvalidConnectionString();

                if (LoopbackHostRewriter.RunningInContainer)
                    value = LoopbackHostRewriter.Rewrite(value);

                if (!BlobClientFactory.TryCreate(value, out var client))
                    return InvalidConnectionString();

                await connection.UseAsync(client, request.AllowWrites, cancellationToken);

                return Results.Ok(connection.Info);
            })
            .AddEndpointFilter<RequireExplorerHeaderFilter>();

        api.MapDelete("/connection", (IStorageConnection connection) =>
            {
                connection.Reset();

                return Results.Ok(connection.Info);
            })
            .AddEndpointFilter<RequireExplorerHeaderFilter>();

        api.MapGet("/containers", async (IBlobExplorerService explorer, CancellationToken cancellationToken) =>
            Results.Ok(await explorer.ListContainersAsync(cancellationToken)));

        api.MapGet("/containers/{container}/entries", async (
            string container,
            string? prefix,
            IBlobExplorerService explorer,
            CancellationToken cancellationToken) =>
            Results.Ok(await explorer.ListEntriesAsync(container, prefix, cancellationToken)));

        api.MapGet("/containers/{container}/blob", async (
            string container,
            string path,
            IBlobExplorerService explorer,
            CancellationToken cancellationToken) =>
        {
            var download = await explorer.DownloadAsync(container, path, cancellationToken);

            return download is null
                ? Results.NotFound()
                : Results.Stream(download.Content, download.ContentType, download.FileName);
        });

        api.MapDelete("/containers/{container}/blob", async (
                string container,
                string path,
                IBlobExplorerService explorer,
                CancellationToken cancellationToken) =>
            await explorer.DeleteAsync(container, path, cancellationToken)
                ? Results.NoContent()
                : Results.NotFound())
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        return app;
    }

    private static IResult InvalidConnectionString() =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid connection string",
            detail: "Expected a storage account connection string, for example " +
                    "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net. " +
                    "A bare service URI is not supported.");
}
