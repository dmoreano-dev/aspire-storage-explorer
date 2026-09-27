# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/) and the
versions follow [SemVer](https://semver.org/). The release workflow publishes the section that matches the tag, so set
the date when you tag.

## [Unreleased]

### Added

- Screenshots in the README (`assets/screenshots`).

### Fixed

- Download and delete failed for a blob that existed, and deleted nothing, when the emulator was reached by a name
  (`localhost`, `host.docker.internal`) on a port other than 10000 to 10002: "not found" for a blob in a folder and "bad
  request" for one at the root of the container. It happened, for example, with an Azurite on a custom port added in
  **Change connection**. Listing was not affected. The Azure SDK took the account in the address for the container, so
  it built the address of the blob without its container.

## [0.4.0] - 2026-09-26

### Added

- **Whole container** next to the search box: the search can now cover the whole container, and not only the folder you
  are in and everything below it.
- Press `/` to jump to the container search.
- A theme button next to Refresh cycles **System**, **Light** and **Dark**. System, the default, follows the theme of the
  operating system as before. The choice is kept in a cookie, not in `localStorage`, because Aspire gives the explorer a
  different port on each run and a cookie is shared by every port of `localhost`.

### Changed

- New look for the page. The account and Refresh are in the top bar, and the state of the connection is in the status bar
  at the bottom: green for a local account, blue for a remote one that is read-only, and red, with a banner, for a
  remote one you allowed changes on. Files show a colored tile by type, Download and Delete appear when you point at a
  row, dates read "Today, 9:14 AM" (the full date is in the tooltip). The page makes no requests to other sites: it
  uses the fonts of the system. The colors use `light-dark()`, so it needs Chrome or Edge 123, Firefox 120 or Safari 17.5,
  or later.
- Deleting on an account that is not on your machine asks you to type the name of the blob. The confirmation now lists
  the account, container, blob and size.
- A **Blobs | Queues | Tables** switch sits above the list of containers. Only Blobs works: Queues and Tables are
  disabled until they are implemented.

## [0.3.0] - 2026-09-26

### Added

- Search by name: type in the box above the list to find the blobs whose name contains the text (not case sensitive) in
  the folder you are in and in every folder below it. A result shows its path below that folder, and the folder part
  links to where the blob is. Opening another folder clears the search. The search reads at most 50,000 blobs and shows
  at most 5,000 matches; when it stops at one of those limits the page says so. It is served by
  `GET /api/containers/{container}/search?prefix=&q=`.
- Search containers: a box above the container list narrows it by name (not case sensitive), and Enter opens the first
  match. It is cleared when you change the connection.
- Sort the list by name, size, type or last modified date by clicking a column header; click it again to reverse.
  Folders always stay on top. The sort is kept when you open another folder.

### Changed

- The default order is now by name in natural order (`file2` before `file10`, and not case sensitive). Before, it was
  the order the storage account returns, which is by character code.

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
