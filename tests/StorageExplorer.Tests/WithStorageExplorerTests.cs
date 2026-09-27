using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace StorageExplorer.Web.Tests;

/// <summary>
/// What <c>WithStorageExplorer()</c> adds to the application model. It reads the model and never starts a container,
/// so it needs no Docker.
/// </summary>
public class WithStorageExplorerTests
{
    private const string ConnectionString = "StorageExplorer__ConnectionString";
    private const string ReadOnly = "StorageExplorer__ReadOnly";

    [Fact]
    public void WithStorageExplorer_DefaultName_UsesStorageNameWithExplorerSuffix()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        storage.WithStorageExplorer();

        // Assert
        Assert.Equal("storage-explorer", Explorer(builder).Name);
    }

    [Fact]
    public void WithStorageExplorer_ContainerName_UsesTheGivenName()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        storage.WithStorageExplorer(containerName: "files");

        // Assert
        Assert.Equal("files", Explorer(builder).Name);
    }

    [Fact]
    public void WithStorageExplorer_Image_UsesPublishedImageAtPackageVersion()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // The tag has to follow the package version: a package that runs another version of the explorer than the one
        // it was released with is a broken release.
        var packageVersion = typeof(StorageExplorerResource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

        // Act
        storage.WithStorageExplorer();

        // Assert
        var image = Explorer(builder).Annotations.OfType<ContainerImageAnnotation>().Single();
        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal("dmoreano-dev/storage-explorer", image.Image);
        Assert.Equal(packageVersion, image.Tag);
    }

    [Fact]
    public void WithStorageExplorer_Endpoint_TargetsPortOfTheImage()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        storage.WithStorageExplorer();

        // Assert
        var endpoint = Explorer(builder).Annotations.OfType<EndpointAnnotation>().Single();
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
    }

    [Fact]
    public void WithStorageExplorer_Storage_ExplorerWaitsForIt()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        storage.WithStorageExplorer();

        // Assert
        var wait = Explorer(builder).Annotations.OfType<WaitAnnotation>().Single();
        Assert.Same(storage.Resource, wait.Resource);
    }

    [Fact]
    public void WithStorageExplorer_Call_ReturnsTheStorageBuilder()
    {
        // Arrange
        var builder = CreateBuilder();
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        var actual = storage.WithStorageExplorer();

        // Assert
        Assert.Same(storage, actual);
    }

    [Fact]
    public void WithStorageExplorer_NullBuilder_ThrowsArgumentNullException()
    {
        // Act
        var actual = () => StorageExplorerBuilderExtensions.WithStorageExplorer(null!);

        // Assert
        Assert.Throws<ArgumentNullException>(actual);
    }

    [Fact]
    public void WithStorageExplorer_PublishMode_AddsNothing()
    {
        // Arrange
        var builder = CreateBuilder(publish: true);
        var storage = builder.AddAzureStorage("storage").RunAsEmulator();

        // Act
        var actual = storage.WithStorageExplorer();

        // Assert
        Assert.Same(storage, actual);
        Assert.Empty(builder.Resources.OfType<StorageExplorerResource>());
    }

    [Fact]
    public async Task WithStorageExplorer_ReadOnlyNotSet_DoesNotSendVariable()
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddAzureStorage("storage").RunAsEmulator().WithStorageExplorer();

        // Act
        var actual = await Environment(builder);

        // Assert
        Assert.False(actual.ContainsKey(ReadOnly));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task WithStorageExplorer_ReadOnlySet_SendsVariableAsSet(bool readOnly, string expected)
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddAzureStorage("storage").RunAsEmulator().WithStorageExplorer(readOnly: readOnly);

        // Act
        var actual = await Environment(builder);

        // Assert
        Assert.Equal(expected, actual[ReadOnly]);
    }

    [Fact]
    public async Task WithStorageExplorer_NoEmulatorAndNoConnectionString_FailsWithHelpfulMessage()
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddAzureStorage("storage").WithStorageExplorer();

        // Act
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Configure(builder));

        // Assert
        Assert.Contains("storage", actual.Message);
        Assert.Contains("RunAsEmulator()", actual.Message);
        Assert.Contains(ConnectionString, actual.Message);
    }

    [Fact]
    public async Task WithStorageExplorer_NoEmulatorButConnectionStringFromConfigure_UsesIt()
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddAzureStorage("storage").WithStorageExplorer(
            configureContainer: explorer => explorer.WithEnvironment(ConnectionString, "UseDevelopmentStorage=true"));

        // Act
        var actual = await Environment(builder);

        // Assert
        Assert.Equal("UseDevelopmentStorage=true", actual[ConnectionString]);
    }

    [Fact]
    public async Task WithStorageExplorer_ConnectionStringFromConfigure_ReplacesEmulatorOne()
    {
        // Arrange
        var builder = CreateBuilder();
        builder.AddAzureStorage("storage").RunAsEmulator().WithStorageExplorer(
            configureContainer: explorer => explorer.WithEnvironment(ConnectionString, "from-configure-container"));

        // Act
        var actual = await Environment(builder);

        // Assert
        Assert.Equal("from-configure-container", actual[ConnectionString]);
    }

    private static IDistributedApplicationBuilder CreateBuilder(bool publish = false)
    {
        var builder = publish
            ? DistributedApplication.CreateBuilder(["--operation", "publish", "--publisher", "manifest", "--output-path", Path.GetTempPath()])
            : DistributedApplication.CreateBuilder();

        Assert.Equal(publish, builder.ExecutionContext.IsPublishMode);

        return builder;
    }

    private static StorageExplorerResource Explorer(IDistributedApplicationBuilder builder) =>
        Assert.Single(builder.Resources.OfType<StorageExplorerResource>());

    private static async Task<IExecutionConfigurationResult> Configure(IDistributedApplicationBuilder builder)
    {
        // Building creates the service provider that resolving a value needs. It does not start anything.
        await using var app = builder.Build();

        return await ExecutionConfigurationBuilder
            .Create(Explorer(builder))
            .WithEnvironmentVariablesConfig()
            .BuildAsync(builder.ExecutionContext);
    }

    // What WithStorageExplorer registers for the environment only runs when the environment is resolved, so resolving it
    // is the Act of the tests that check what is sent to the container.
    private static async Task<Dictionary<string, string>> Environment(IDistributedApplicationBuilder builder)
    {
        var configuration = await Configure(builder);

        Assert.Null(configuration.Exception);

        return configuration.EnvironmentVariables.ToDictionary(variable => variable.Key, variable => variable.Value);
    }
}
