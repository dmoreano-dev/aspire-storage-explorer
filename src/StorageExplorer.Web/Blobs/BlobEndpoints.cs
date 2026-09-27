using StorageExplorer.Web.Endpoints;

namespace StorageExplorer.Web.Blobs;

internal static class BlobEndpoints
{
    public static IEndpointRouteBuilder MapBlobEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/blobs");

        api.MapGet("/containers", async (IBlobExplorerService explorer, CancellationToken cancellationToken) =>
            Results.Ok(await explorer.ListContainersAsync(cancellationToken)));

        api.MapGet("/containers/{container}/entries", async (
            string container,
            string? prefix,
            IBlobExplorerService explorer,
            CancellationToken cancellationToken) =>
            Results.Ok(await explorer.ListEntriesAsync(container, prefix, cancellationToken)));

        api.MapGet("/containers/{container}/search", async (
            string container,
            string? prefix,
            string? q,
            IBlobExplorerService explorer,
            CancellationToken cancellationToken) =>
        {
            var term = q?.Trim();

            return string.IsNullOrEmpty(term)
                ? Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Missing search text",
                    detail: "Pass the text to look for in the q query parameter.")
                : Results.Ok(await explorer.SearchAsync(container, prefix, term, cancellationToken));
        });

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
}
