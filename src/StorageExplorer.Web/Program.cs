using StorageExplorer.Web;
using StorageExplorer.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// The options are built more than once (startup validation and the client), so log the rewrite only once.
var rewriteLogged = false;

builder.Services
    .AddOptions<StorageExplorerOptions>()
    .BindConfiguration(StorageExplorerOptions.SectionName)
    .PostConfigure<ILoggerFactory>((options, loggerFactory) =>
    {
        if (!LoopbackHostRewriter.RunningInContainer)
            return;

        var rewritten = LoopbackHostRewriter.Rewrite(options.ConnectionString);
        if (rewritten == options.ConnectionString)
            return;

        if (!rewriteLogged)
        {
            rewriteLogged = true;
            loggerFactory.CreateLogger("StorageExplorer").LogInformation(
                "The connection string points to localhost, which is this container. Using {Host} to reach the host machine instead.",
                LoopbackHostRewriter.HostGateway);
        }

        options.ConnectionString = rewritten;
    })
    .ValidateDataAnnotations()
    .Validate(
        options => IsStorageConnectionString(options.ConnectionString),
        "StorageExplorer:ConnectionString must be a storage account connection string " +
        "(for example DefaultEndpointsProtocol=...;AccountName=...;AccountKey=...;BlobEndpoint=...). " +
        "A bare service URI is not supported yet.")
    .ValidateOnStart();

builder.Services.AddSingleton<IStorageConnection, StorageConnection>();
builder.Services.AddSingleton<IBlobExplorerService, BlobExplorerService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StorageExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapExplorerEndpoints();

app.Run();

// An empty value is left to the [Required] attribute so it does not get two messages.
static bool IsStorageConnectionString(string connectionString) =>
    string.IsNullOrEmpty(connectionString) || BlobClientFactory.TryCreate(connectionString, out _);
