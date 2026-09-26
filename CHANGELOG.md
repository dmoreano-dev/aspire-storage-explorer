# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/) and the
versions follow [SemVer](https://semver.org/). The release workflow publishes the section that matches the tag, so set
the date when you tag.

## [0.1.1] - 2026-09-26

### Changed

- Lowered the minimum `Aspire.Hosting.Azure.Storage` version from 13.5.4 to 13.1.0, so the package installs in
  AppHosts on Aspire 13.1 or later. Before, an AppHost on an older Aspire 13 failed to restore with `NU1605`
  (package downgrade).

## [0.1.0] - 2026-09-26

First release.

### Added

- Web UI to browse an Azure Blob Storage account: containers, folders with a breadcrumb, and blobs with name, size,
  content type and last modified date.
- Download a blob, and delete a single blob after a confirmation that names the account.
- `WithStorageExplorer()` on the Azure Storage resource, which adds the explorer container to the AppHost. With the
  emulator the connection string is set for you.
- Point the explorer at another account: a connection string from `configureContainer`, or **Change connection** on
  the page. `localhost` is mapped to `host.docker.internal` inside the container.
- Guards for a tool without authentication: not added when publishing, `Host` filtering, no CORS, and a custom header
  plus same-origin check on delete and on changing the connection.
- Sample AppHost with Azurite and seeded data. Turn the seeding off with `Seed:Enabled=false`.

### Known limitations

- Blob Storage only (no queues, tables or file shares).
- Account connection strings with a key only; Entra ID is not supported.
- No read-only mode, so delete works on whatever account is connected.
- Each folder listing is capped at 5,000 entries.
