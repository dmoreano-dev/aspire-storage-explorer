namespace StorageExplorer.Web.Endpoints;

/// <summary>
/// Guards the endpoints that change data in the storage account. It goes after <see cref="RequireExplorerHeaderFilter"/>,
/// and it is what keeps a read-only connection read-only: the page only hides the buttons.
/// </summary>
internal sealed class RequireWritableFilter(IStorageConnection connection) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var info = connection.Info;

        if (!info.ReadOnly)
            return next(context);

        var detail = info.ReadOnlyLocked
            ? "readOnly is set in WithStorageExplorer, so this explorer only reads."
            : "This account is not on this machine, so it is read-only. Tick \"Allow changes\" in Change connection, " +
              "or pass readOnly: false to WithStorageExplorer.";

        return ValueTask.FromResult<object?>(
            Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The explorer is read-only", detail: detail));
    }
}
