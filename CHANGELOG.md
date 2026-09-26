# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/) and the
versions follow [SemVer](https://semver.org/). The release workflow publishes the section that matches the tag, so set
the date when you tag.

## [Unreleased]

## [0.2.0] - 2026-09-26

### Added

- `WithStorageExplorer(readOnly: true)` locks the explorer to reading: deleting answers `403` and the page hides Delete
  and cannot lift it. Download and browsing still work.
- **Allow changes on this account, at my own risk** in **Change connection**, for an account that is not on your
  machine. The page shows a **Read-only** or **Writable · own risk** badge next to the account.

### Changed

- An account that is not on your machine (anything but `localhost`, `127.0.0.1`, `[::1]`, `host.docker.internal` or a
  container name) is read-only by default. Before, deleting worked on whatever account you connected. To delete on a
  remote account, pass `readOnly: false` (for the connection from the AppHost) or tick **Allow changes** when you connect
  to it from the page. The emulator is not affected.
- `WithStorageExplorer` has a new optional parameter, `readOnly`, after `containerName`. It is source compatible, but a
  project compiled against 0.1.x must be rebuilt.
- `GET /api/connection` also returns `isLocal`, `readOnly` and `readOnlyLocked`, and `PUT /api/connection` accepts
  `allowWrites`.

## [0.1.2] - 2026-09-26

### Fixed

- The explorer could not read the emulator: every request failed with an empty `400 Bad Request` from Azurite, both
  with the connection string from the AppHost and with one typed in **Change connection**. Aspire gives containers the
  host `storage.dev.internal`, and Azurite reads a host with a dot as `{account}.blob...`, so it looked for an account
  called `storage`. A `{name}.dev.internal` host in the connection string is now used as `{name}`, which is also a
  network alias of the container and which Azurite accepts.

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
