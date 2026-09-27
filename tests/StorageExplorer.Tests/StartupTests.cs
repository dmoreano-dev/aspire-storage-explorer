using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace StorageExplorer.Web.Tests;

/// <summary>The app as the container starts it: configuration in, real connection object, no fakes.</summary>
public class StartupTests
{
    [Fact]
    public async Task Startup_ValidConnectionString_AnswersWithTheAccount()
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.Local);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("devstoreaccount1", actual.GetProperty("accountName").GetString());
        Assert.True(actual.GetProperty("isLocal").GetBoolean());
        Assert.False(actual.GetProperty("readOnly").GetBoolean());
        Assert.False(actual.GetProperty("isCustom").GetBoolean());
    }

    [Theory]
    [InlineData("true", true, true)]
    [InlineData("false", false, false)]
    public async Task Startup_ReadOnlySetting_ReflectsInConnectionInfo(string value, bool readOnly, bool locked)
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.Local, readOnly: value);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal(readOnly, actual.GetProperty("readOnly").GetBoolean());
        Assert.Equal(locked, actual.GetProperty("readOnlyLocked").GetBoolean());
    }

    [Fact]
    public async Task Startup_RemoteAccountWithoutReadOnlySetting_IsReadOnly()
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.Remote);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.False(actual.GetProperty("isLocal").GetBoolean());
        Assert.True(actual.GetProperty("readOnly").GetBoolean());
    }

    [Fact]
    public async Task Startup_MissingConnectionString_Fails()
    {
        // Act
        var actual = await ExpectValidationFailureAsync(() => Start(connectionString: null));

        // Assert
        Assert.Contains("ConnectionString", actual.Message);
    }

    [Theory]
    [InlineData("not a connection string")]
    [InlineData("https://prodaccount.blob.example.invalid")]
    [InlineData("AccountName=onlyaname")]
    public async Task Startup_InvalidConnectionString_Fails(string value)
    {
        // Act
        var actual = await ExpectValidationFailureAsync(() => Start(value));

        // Assert
        Assert.Contains("must be a storage account connection string", actual.Message);
    }

    [Fact]
    public async Task Startup_InvalidConnectionString_DoesNotRepeatItInTheError()
    {
        // Act
        var actual = await ExpectValidationFailureAsync(() => Start("AccountName=x;AccountKey=SUPER-SECRET-KEY;Nonsense=1"));

        // Assert
        Assert.DoesNotContain("SUPER-SECRET-KEY", actual.ToString());
    }

    // ---- inside and outside a container ----

    [Fact]
    public async Task Startup_InContainerWithDevInternalHost_UsesThePlainContainerName()
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.DevInternal, runningInContainer: true);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("storage:10000", actual.GetProperty("endpoint").GetString());
        Assert.True(actual.GetProperty("isLocal").GetBoolean());
    }

    [Fact]
    public async Task Startup_OutsideContainerWithDevInternalHost_KeepsTheHost()
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.DevInternal, runningInContainer: false);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("storage.dev.internal:10000", actual.GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task Startup_InContainerWithLoopbackHost_UsesTheHostMachine()
    {
        // Arrange
        await using var factory = Start(TestConnectionStrings.Local, runningInContainer: true);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("host.docker.internal:1", actual.GetProperty("endpoint").GetString());
        Assert.True(actual.GetProperty("isLocal").GetBoolean());
    }

    [Fact]
    public async Task Startup_InContainerWithDevelopmentStorageShortcut_UsesTheEmulatorOnTheHostMachine()
    {
        // Arrange
        await using var factory = Start("UseDevelopmentStorage=true", runningInContainer: true);
        using var client = factory.CreateClient();

        // Act
        var actual = await client.GetFromJsonAsync<JsonElement>("/api/connection");

        // Assert
        Assert.Equal("devstoreaccount1", actual.GetProperty("accountName").GetString());
        Assert.Equal("host.docker.internal:10000", actual.GetProperty("endpoint").GetString());
    }

    [Fact]
    public async Task Startup_InContainerWithRewrittenConnectionString_LogsItOnce()
    {
        // Arrange
        var logger = new CollectingLoggerProvider();
        await using var factory = Start(TestConnectionStrings.Local, runningInContainer: true, logger: logger);
        using var client = factory.CreateClient();

        // Act
        // The options are built once at startup and again when the connection is created.
        await client.GetAsync("/api/connection");

        // Assert
        var rewrites = logger.Entries.Where(e => e.Category == "StorageExplorer" && e.Message.Contains("host.docker.internal"));
        Assert.Single(rewrites);
    }

    [Fact]
    public async Task Startup_OutsideContainer_LogsNoRewrite()
    {
        // Arrange
        var logger = new CollectingLoggerProvider();
        await using var factory = Start(TestConnectionStrings.Local, runningInContainer: false, logger: logger);
        using var client = factory.CreateClient();

        // Act
        await client.GetAsync("/api/connection");

        // Assert
        Assert.DoesNotContain(logger.Entries, e => e.Category == "StorageExplorer");
    }

    [Fact]
    public async Task Startup_NoReplacement_UsesTheRealContainerEnvironment()
    {
        // Arrange
        // The other tests replace the check, so this is the one that says the app registers the real one.
        await using var factory = Start(TestConnectionStrings.Local, runningInContainer: null);
        using var client = factory.CreateClient();

        // Act
        var actual = factory.Services.GetRequiredService<IContainerEnvironment>();

        // Assert
        Assert.IsType<ContainerEnvironment>(actual);
    }

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> is racy when startup fails through
    /// <c>ValidateOnStart()</c>: <c>DeferredHost.StartAsync</c> disposes its service provider in a <c>finally</c>
    /// block on the host's own thread while this thread is still reading a service from it to build the test server,
    /// which occasionally throws <see cref="ObjectDisposedException"/> instead of surfacing the
    /// <see cref="OptionsValidationException"/> the failed validation actually raised. This is a known issue in
    /// <c>Microsoft.AspNetCore.Mvc.Testing</c> itself (see <see href="https://github.com/dotnet/aspnetcore/issues/58442"/>) 
    /// a fresh factory gets a few more tries at the non-raced path before this gives up and lets the exception through for real.
    /// </summary>
    private static async Task<OptionsValidationException> ExpectValidationFailureAsync(
        Func<WebApplicationFactory<Program>> createFactory)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var factory = createFactory();

            try
            {
                return Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
            }
            catch (ObjectDisposedException) when (attempt < 5)
            {
            }
        }
    }

    /// <param name="connectionString">The setting the AppHost gives, or <c>null</c> for none.</param>
    /// <param name="readOnly">The <c>ReadOnly</c> setting, or <c>null</c> for none.</param>
    /// <param name="runningInContainer">
    /// Whether the app thinks it runs in a container. <c>null</c> leaves the real check in place, which reads the
    /// machine that runs the tests.
    /// </param>
    /// <param name="logger">Receives what the app logs.</param>
    private static WebApplicationFactory<Program> Start(
        string? connectionString,
        string? readOnly = null,
        bool? runningInContainer = false,
        ILoggerProvider? logger = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");

            if (connectionString is not null)
                builder.UseSetting("StorageExplorer:ConnectionString", connectionString);

            if (readOnly is not null)
                builder.UseSetting("StorageExplorer:ReadOnly", readOnly);

            if (runningInContainer is { } value)
                builder.UseContainerEnvironment(value);

            if (logger is not null)
                builder.ConfigureLogging(logging => logging.AddProvider(logger));
        });
}
