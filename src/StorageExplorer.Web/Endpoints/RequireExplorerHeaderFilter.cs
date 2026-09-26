namespace StorageExplorer.Web.Endpoints;

/// <summary>
/// Guards destructive endpoints. The explorer has no login, so a request must carry a custom header
/// (which browsers only send from same-origin scripts, as CORS is not enabled) and, when the browser
/// reports an <c>Origin</c>, that origin must be the host serving the request.
/// </summary>
internal sealed class RequireExplorerHeaderFilter : IEndpointFilter
{
    public const string HeaderName = "X-Storage-Explorer";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;

        if (!request.Headers.ContainsKey(HeaderName))
            return Reject(StatusCodes.Status400BadRequest, $"Missing required header '{HeaderName}'.");

        var origin = request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsSameOrigin(origin, request))
            return Reject(StatusCodes.Status403Forbidden, "Cross-origin requests are not allowed.");

        return next(context);
    }

    private static bool IsSameOrigin(string origin, HttpRequest request) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);

    private static ValueTask<object?> Reject(int statusCode, string title) =>
        ValueTask.FromResult<object?>(Results.Problem(statusCode: statusCode, title: title));
}
