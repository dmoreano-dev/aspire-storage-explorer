# Storage Explorer

An [Aspire](https://aspire.dev) hosting integration that adds a small web UI to browse an Azure Blob Storage account.
It is meant for local development with the Azurite emulator, so you don't need a separate storage explorer.

**Features (v0.1):** list containers, navigate folders with a breadcrumb, list blobs (name, size, content type, last
modified), download a blob, and delete a blob after a confirmation that names the account.

**Requirements:** .NET 10, Aspire 13.1 or later and Docker (the explorer runs as a container next to the emulator).
Keep every `Aspire.*` package of your AppHost at the same version as its SDK (`Aspire.AppHost.Sdk`): mixing versions
fails at startup with errors such as `Unable to resolve service for type 'Aspire.Hosting.Publishing.IContainerRuntime'`.

## Usage

Add the package to your AppHost project:

```bash
dotnet add package StorageExplorer.Aspire.Hosting
```

Then add the explorer to the storage resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator();

storage.WithStorageExplorer();

builder.Build().Run();
```

This adds a `storage-explorer` resource; open its **Storage Explorer** link from the Aspire dashboard.
`WithStorageExplorer` also takes a `configureContainer` callback (image, endpoint, environment) and a `containerName`.

### Another account

With the emulator the connection string is set for you. To browse another account, pass a full account connection
string from `configureContainer` (it replaces the emulator one), preferably through a secret parameter:

```csharp
var connection = builder.AddParameter("explorer-connection", secret: true);

storage.WithStorageExplorer(c => c.WithEnvironment("StorageExplorer__ConnectionString", connection));
```

Set it with `dotnet user-secrets set "Parameters:explorer-connection" "<connection string>"`.

You can also change the account from the page (**Change connection**), even on a running explorer. The connection is
checked before switching, and **Use the AppHost connection** goes back to the default.

Inside the container `localhost` is the container itself, so `localhost`, `127.0.0.1`, `[::1]` and
`UseDevelopmentStorage=true` are mapped to `host.docker.internal`. That lets you reuse the connection string of an
Azurite or Azure Storage Explorer running on your machine as is. It relies on Docker Desktop (on Linux Docker Engine you
may need `--add-host=host.docker.internal:host-gateway`, not tested). A `<name>.dev.internal` host, which is how Aspire
names a container for the others, is used as `<name>`: Azurite reads a host with a dot as `<account>.blob...` and
rejects it with an empty 400. Connection strings for a real Azure account are left untouched.

## Try the sample

```bash
dotnet run --project samples/Sample.AppHost
```

The sample starts Azurite, seeds a few containers with nested folders, and adds the explorer. To skip the seeding, set
`Seed:Enabled` to `false`:

```bash
dotnet run --project samples/Sample.AppHost -- --Seed:Enabled=false
```

The explorer image is pulled from GitHub Container Registry. To try local changes to the web app instead, build the image
first; it gets the same tag the extension uses, so the sample runs it:

```bash
./scripts/build-image.sh
```

## Security

- `WithStorageExplorer` does nothing when the application is published, so it is never deployed.
- Only requests whose `Host` is `localhost`, `127.0.0.1` or `[::1]` are accepted (`AllowedHosts`; override it with an
  environment variable if you need another host name). CORS is not enabled.
- Deleting a blob or changing the connection requires a custom `X-Storage-Explorer` header plus a same-origin `Origin`,
  so other websites cannot do it through your browser.
- A connection set from the page lives in memory only: it is lost on restart, never sent back to the browser, and not
  logged. The page shows the account name and endpoint, not the key.

## Limitations

- Blob Storage only (no queues, tables or file shares). Each folder listing is capped at 5,000 entries.
- Keep the storage resource on `RunAsEmulator()`. Without it Aspire provisions Azure resources and the explorer waits
  for them.
- Only account connection strings with a key work; Entra ID is not supported yet.
- There is no read-only mode: deleting works on whatever account you connect. Only single blobs can be deleted, not
  folders.

## Repository layout

| Path | Description |
| --- | --- |
| `src/StorageExplorer.Web` | ASP.NET Core minimal API plus a static page (no build step), and the `Dockerfile` |
| `src/StorageExplorer.Aspire.Hosting` | The Aspire extension, packed as the NuGet package `StorageExplorer.Aspire.Hosting` |
| `samples/Sample.AppHost` | Aspire app that uses the extension |
| `samples/Sample.Seeder` | Fills the emulator with sample blobs |

See [docs/backlog.md](docs/backlog.md) for what is planned.

## License

[MIT](LICENSE)
