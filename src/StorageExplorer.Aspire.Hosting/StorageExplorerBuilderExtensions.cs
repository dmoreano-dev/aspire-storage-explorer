using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>Extension methods for adding the Storage Explorer to an Azure Storage resource.</summary>
public static class StorageExplorerBuilderExtensions
{
    // Public, well-known credentials of the Azurite emulator (not a secret).
    private const string EmulatorAccountName = "devstoreaccount1";
    private const string EmulatorAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>
    /// Adds a web UI to browse the given Azure Storage account: blobs, with download and delete (delete can be turned
    /// off), and queues and tables, read-only for now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With the storage emulator (<c>RunAsEmulator()</c>) the connection string is set automatically. A real Azure
    /// account needs Entra ID authentication, which the explorer does not implement yet, but you can give it a full
    /// account connection string yourself from <paramref name="configureContainer"/>:
    /// <c>c =&gt; c.WithEnvironment("StorageExplorer__ConnectionString", connectionString)</c>. That value also
    /// replaces the emulator one.
    /// </para>
    /// <para>
    /// Deleting a blob is allowed on the emulator and on other local endpoints. An account that is not on this machine
    /// is read-only, unless <paramref name="readOnly"/> says otherwise, or the user ticks <c>Allow changes</c> when
    /// connecting to it from the page. Queues and tables have no destructive actions yet, so <paramref name="readOnly"/>
    /// does not apply to them.
    /// </para>
    /// <para>
    /// The explorer is a development tool: it has no authentication and is not added when the
    /// application is published.
    /// </para>
    /// </remarks>
    /// <param name="builder">The Azure Storage resource builder.</param>
    /// <param name="configureContainer">Optional callback to customize the explorer container (image, port, ...).</param>
    /// <param name="containerName">The name of the explorer resource. Defaults to <c>{storage-name}-explorer</c>.</param>
    /// <param name="readOnly">
    /// <c>true</c> locks the explorer to reading (no delete), whatever the account, and the page cannot lift it.
    /// <c>false</c> makes the connection from the AppHost writable even when it is not local; a remote account typed in
    /// the page still asks for <c>Allow changes</c>. Leave it unset to allow changes only on local endpoints.
    /// </param>
    /// <returns>The Azure Storage resource builder, for chaining.</returns>
    public static IResourceBuilder<AzureStorageResource> WithStorageExplorer(
        this IResourceBuilder<AzureStorageResource> builder,
        Action<IResourceBuilder<StorageExplorerResource>>? configureContainer = null,
        string? containerName = null,
        bool? readOnly = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
            return builder;

        var storage = builder.Resource;
        var explorer = new StorageExplorerResource(containerName ?? $"{storage.Name}-explorer");

        var explorerBuilder = builder.ApplicationBuilder.AddResource(explorer)
            .WithImage(StorageExplorerContainerImageTags.Image, StorageExplorerContainerImageTags.Tag)
            .WithHttpEndpoint(targetPort: StorageExplorerResource.ContainerPort, name: StorageExplorerResource.HttpEndpointName)
            // Resolved when the explorer starts, so it does not matter whether RunAsEmulator() comes before or after.
            .WithEnvironment(context =>
            {
                if (storage.IsEmulator)
                    context.EnvironmentVariables[StorageExplorerResource.ConnectionStringVariable] = GetEmulatorConnectionString(storage);
            })
            .WithUrlForEndpoint(StorageExplorerResource.HttpEndpointName, url => url.DisplayText = "Storage Explorer")
            .WithParentRelationship(builder)
            .WaitFor(builder)
            .ExcludeFromManifest();

        if (StorageExplorerContainerImageTags.Registry is { } registry)
            explorerBuilder.WithImageRegistry(registry);

        if (readOnly is { } value)
            explorerBuilder.WithEnvironment(StorageExplorerResource.ReadOnlyVariable, value ? "true" : "false");

        configureContainer?.Invoke(explorerBuilder);

        // Registered after configureContainer, so it runs last and only fails when neither the emulator nor the
        // caller provided a connection string.
        explorerBuilder.WithEnvironment(context =>
        {
            if (!context.EnvironmentVariables.ContainsKey(StorageExplorerResource.ConnectionStringVariable))
            {
                throw new InvalidOperationException(
                    $"The Storage Explorer has no connection string for '{storage.Name}': only the storage emulator is " +
                    "supported automatically. Call RunAsEmulator() on the storage resource, or set the " +
                    $"{StorageExplorerResource.ConnectionStringVariable} environment variable from configureContainer.");
            }
        });

        return builder;
    }

    // The account connection string is not public on AzureStorageResource, so it is built from the public
    // service URIs. It carries the three services, so queues and tables can be added later without changes here.
    private static ReferenceExpression GetEmulatorConnectionString(AzureStorageResource storage) =>
        ReferenceExpression.Create(
            $"DefaultEndpointsProtocol=http;AccountName={EmulatorAccountName};AccountKey={EmulatorAccountKey};" +
            $"BlobEndpoint={storage.BlobUriExpression}/{EmulatorAccountName};" +
            $"QueueEndpoint={storage.QueueUriExpression}/{EmulatorAccountName};" +
            $"TableEndpoint={storage.TableUriExpression}/{EmulatorAccountName};");
}
