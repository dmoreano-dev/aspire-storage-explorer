# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/) and the
versions follow [SemVer](https://semver.org/). The release workflow publishes the section that matches the tag, so set
the date when you tag.

## [Unreleased]

## [0.7.0] - 2026-09-28

### Added

- Destructive actions for Queues and Tables, all behind the same guards as blob delete
  (`RequireExplorerHeaderFilter` and `RequireWritableFilter`):
  - **Delete queue** and **delete table**, from the sidebar row on hover.
  - **Clear queue**, next to the peeked messages, which removes every message in the queue, including ones beyond
    the 32-message peek window.
  - **Delete peeked messages**, which removes only what is currently peeked (up to 32). The backlog originally asked
    for deleting a single message, but that requires receiving it first (peeking never returns a pop receipt), and
    receiving cannot target one specific message — only "whatever's at the front, up to N." Deleting one message
    that way would have raised the `DequeueCount` of every other message pulled into that batch, even though it was
    put right back untouched. Deleting the whole peeked batch instead removes that side effect entirely, since
    nothing is received and then released — only received and then deleted. To guard against the batch drifting from
    what was last shown on screen (`Receive` and `Peek` are independent calls, and Azure does not guarantee they see
    the same messages if the queue is being used by something else), the server peeks again immediately before
    receiving and only deletes a message confirmed present by that fresh peek, releasing anything else received
    unharmed.
  - **Delete entity**, from its row in the entity table.
- The confirm dialog (previously blob-only) is now generic: title, warning text and details vary by action, but the
  remote-account "type the name to confirm" behavior is unchanged.

## [0.6.0] - 2026-09-27

### Added

- Blobs, Queues and Tables refresh themselves in the background instead of needing the Refresh button for a write
  made from outside the tool (the CLI, Azure Storage Explorer, another process, or a teammate on a shared account).
  Azure Storage (and Azurite) has no push notification to subscribe to, so this polls instead: it stays scoped to
  whatever is on screen (the sidebar list for the active service, and the listing, peek or query the active item
  shows), asks again every 8 seconds, pauses the moment the tab is hidden and catches up as soon as it is visible
  again. A table with rows loaded past the first page via **Load more** is left alone by it, so that state is not
  thrown away by a timer.
- **Copy Message Id** next to **Copy body** / **Copy raw** on an expanded queue message.

### Changed

- More space below the "peeking does not change the queue" note banner.

## [0.5.0] - 2026-09-27

### Added

- Queues and Tables, read-only, behind a new **Blobs | Queues | Tables** switch above the container list (a service is
  enabled only when the connected account has it).
  - Queues: list queues with their message count; peek up to 32 messages, with a search box and Base64 messages
    decoded automatically. Expand a message to see its full body, its exact dates, and **Copy body** / **Copy raw**.
  - Tables: list tables; query entities with an OData filter and **Load more** paging. Columns are dynamic (the union
    of the keys in the rows shown), typed, and a boolean shows as a colored chip.
- `Sample.Seeder` now also seeds a few queues with messages and tables with entities.

### Changed

- `StorageExplorer.Web` reorganized by service (`Blobs/`, `Queues/`, `Tables/`); the blob API routes moved from
  `/api/containers/...` to `/api/blobs/containers/...`. Internal only, nothing outside this repo depends on them.

### Fixed

- A storage error's detail showed the SDK's whole exception dump (status, error code, raw response, headers) instead
  of just the message.
- A few UI issues found while building the above: a "Querying…" label stuck after a failed table query, a message id
  overflowing into the next column, and table column headers stacking in one column instead of sitting side by side.
- The integration tests' Azurite fixture only built a blob endpoint, so a queue or table client reached out to real
  Azure instead of the emulator.

## [0.4.1] - 2026-09-27

### Added

- Screenshots in the README (`assets/screenshots`).

### Changed

- Pinned a direct `MessagePack` 2.5.302 reference in `StorageExplorer.Aspire.Hosting`, overriding the 2.5.192 that
  `Aspire.Hosting.Azure.Storage` 13.1.0 brings in through `StreamJsonRpc` and clearing the `NU1902`/`NU1903` advisories.
  `Aspire.Hosting.Azure.Storage` itself stays at 13.1.0: every version from 13.2.0 to 13.5.4 (the latest) makes
  `DistributedApplication.CreateBuilder().Build()` hang forever on dispose when the app is never `Run()` — exactly the
  pattern `WithStorageExplorerTests` uses to read what `WithStorageExplorer()` adds without starting a container.
  Bisected with `--blame-hang-timeout`; 13.1.3 is the last good version, 13.2.0 the first bad one. Filed as a
  candidate upstream bug; revisit the floor once it is fixed there.

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
