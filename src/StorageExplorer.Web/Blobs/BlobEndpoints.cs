using Microsoft.AspNetCore.Http.Features;
using StorageExplorer.Web.Endpoints;

namespace StorageExplorer.Web.Blobs;

internal static class BlobEndpoints
{
    public static IEndpointRouteBuilder MapBlobEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/blobs");

        api.MapGet("/containers", async (IBlobExplorerService explorer, CancellationToken cancellationToken) =>
            Results.Ok(await explorer.ListContainersAsync(cancellationToken)));

        api.MapPost("/containers/{container}", async (
                string container,
                IBlobExplorerService explorer,
                CancellationToken cancellationToken) =>
            {
                await explorer.CreateContainerAsync(container, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

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
            CancellationToken cancellationToken,
            bool inline = false) =>
        {
            var download = await explorer.DownloadAsync(container, path, cancellationToken);

            if (download is null)
                return Results.NotFound();

            // inline is for the preview dialog's <img>/<iframe>: omitting fileDownloadName means no
            // Content-Disposition header is sent, so the browser renders the content instead of forcing a
            // save dialog. Missing from the query string, it binds to false, so plain downloads are unchanged.
            return inline
                ? Results.Stream(download.Content, download.ContentType)
                : Results.Stream(download.Content, download.ContentType, download.FileName);
        });

        api.MapPost("/containers/{container}/blob", async (
                string container,
                string path,
                HttpRequest request,
                IBlobExplorerService explorer,
                CancellationToken cancellationToken) =>
            {
                // The upload is the raw request body (drag and drop sends the file itself, not a multipart form), which
                // can be far larger than Kestrel's 30 MB default request body limit; this endpoint has none.
                var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeFeature is { IsReadOnly: false })
                    sizeFeature.MaxRequestBodySize = null;

                await explorer.UploadAsync(container, path, request.Body, request.ContentType, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

        api.MapPost("/containers/{container}/folder", async (
                string container,
                string path,
                IBlobExplorerService explorer,
                CancellationToken cancellationToken) =>
            {
                await explorer.CreateFolderAsync(container, path, cancellationToken);
                return Results.NoContent();
            })
            .AddEndpointFilter<RequireExplorerHeaderFilter>()
            .AddEndpointFilter<RequireWritableFilter>();

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
