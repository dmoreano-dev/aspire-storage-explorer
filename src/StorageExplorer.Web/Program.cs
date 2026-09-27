using StorageExplorer.Web;
using StorageExplorer.Web.Blobs;
using StorageExplorer.Web.Endpoints;
using StorageExplorer.Web.Queues;
using StorageExplorer.Web.Tables;

var builder = WebApplication.CreateBuilder(args);

// The options are built more than once (startup validation and the client), so log the rewrite only once.
var rewriteLogged = false;

builder.Services
    .AddOptions<StorageExplorerOptions>()
    .BindConfiguration(StorageExplorerOptions.SectionName)
    .PostConfigure<ILoggerFactory, IContainerEnvironment>((options, loggerFactory, container) =>
    {
        if (!container.RunningInContainer)
            return;

        var rewritten = LoopbackHostRewriter.Rewrite(options.ConnectionString);
        if (rewritten == options.ConnectionString)
            return;

        if (!rewriteLogged)
        {
            rewriteLogged = true;
            loggerFactory.CreateLogger("StorageExplorer").LogInformation(
                "The connection string has hosts that do not work from this container: localhost is this container, so {Host} " +
                "is used to reach the host machine, and a <name>.dev.internal host is used as <name>, which Azurite accepts.",
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

builder.Services.AddSingleton<IContainerEnvironment, ContainerEnvironment>();
builder.Services.AddSingleton<IStorageConnection, StorageConnection>();
builder.Services.AddSingleton<IBlobExplorerService, BlobExplorerService>();
builder.Services.AddSingleton<IQueueExplorerService, QueueExplorerService>();
builder.Services.AddSingleton<ITableExplorerService, TableExplorerService>();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StorageExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapConnectionEndpoints();
app.MapBlobEndpoints();
app.MapQueueEndpoints();
app.MapTableEndpoints();

app.Run();

// An empty value is left to the [Required] attribute so it does not get two messages.
static bool IsStorageConnectionString(string connectionString) =>
    string.IsNullOrEmpty(connectionString) || StorageClientFactory.TryCreateBlob(connectionString, out _);
