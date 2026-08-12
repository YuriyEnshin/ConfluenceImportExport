# Changelog

[Русский](CHANGELOG.md) | **English**

All notable changes to the Confluence Page Exporter tool are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- The sync report gained an "attachments not synchronised" section: attachments
  that could not be downloaded or uploaded are now listed with the page, file
  name and reason. Such entries raise the issue flag (`hasIssues`), appear in the
  MCP envelope's `summary` (`… ; 1 attachment(s) failed`) and in
  `report.failedAttachments`, and in the CLI a warning about the incomplete
  mirror is printed even without `--report`.

### Fixed

- `download update` / `download merge` no longer report success when an
  attachment failed to download: the failure previously went to the log only —
  no file appeared in the page folder while the report showed
  `hasIssues: false`, so the mirror divergence was visible only through a
  separate `compare`.
- An unreachable attachment listing no longer looks like "the page has no
  attachments" (on Server/DC and on Cloud): the listing error reaches the
  report, the page's attachments are not counted as synchronised, and the
  attachment baselines stored in the marker are not wiped. In this situation
  `upload update` / `upload merge` no longer push attachments blindly, and
  `compare` marks the page as not compared in `Notes`.
- The `compare` summary advice for differing attachments is now chosen by the
  direction of the difference: `OnlyRemote` / `ChangedServer` suggests
  downloading, `OnlyLocal` / `ChangedLocal` suggests uploading, and a conflict
  or a size-only difference suggests manual inspection (previously uploading was
  suggested in every case).
- The version in `Directory.Build.props` was brought in line with the latest
  release (2.18.0): a build from source printed `--version 2.16.0`, even though
  release artifacts were built with the correct version from the tag.

## [2.18.0] — 2026-07-02

### Added

- Write support for Confluence Cloud: `upload update` / `upload create` /
  `upload merge` and all attachment operations now work on Cloud — Cloud
  support is complete. Pages are created and updated via REST API v2 (the
  numeric space id is resolved from the key automatically; a version conflict
  arrives as 409 and is handled as before), attachments are uploaded through
  the v1 endpoints Atlassian kept on Cloud. Verified end-to-end against a
  live Cloud site, including both-sides conflict detection.

### Fixed

- Removed the phantom "changed on server" in `compare` for pages whose macros
  were authored without service attributes: Confluence adds
  `ac:schema-version` to macros on save, and the Cloud editor stamps
  `local-id`/`ac:local-id` onto elements — the normalizer now ignores these
  attributes during comparison (uploaded content is not affected). The
  canonicalization epoch is bumped to 3: hashes stored in markers are
  recomputed on each page's next sync; until then local-edit detection falls
  back to file modification times, once.

## [2.17.0] — 2026-07-02

### Added

- Confluence Cloud support — read operations. With `--auth-type cloud` (or
  auto-detection via `*.atlassian.net`) the tool talks to the Cloud REST API v2
  (`/wiki/api/v2`): `download update`/`download merge`, `compare` (including
  `--detect-source`), and the `confluence_ping` / `confluence_get_page_content`
  MCP tools are available. Cloud's numeric space ids are transparently
  translated to keys — `.id` markers, configuration and tool parameters keep
  using space keys. Write operations (upload update/create/merge, attachment
  changes) are not supported on Cloud yet and fail with a clear error
  (`NOT_SUPPORTED` in MCP); they arrive in upcoming releases.
- Confluence deployment-type auto-detection from `--base-url`: `*.atlassian.net`
  hosts are treated as Cloud, everything else as on-prem; an explicit
  `--auth-type` takes precedence. An invalid `--auth-type` value now fails with
  a clear error instead of being silently ignored, and `config show` displays
  the effective mode (e.g. `cloud (auto-detected)`). The `cloud` mode is
  preparatory for now: the existing (v1) API client is used and a warning is
  printed — full Cloud API support arrives in upcoming releases.

### Changed

- `compare`: when the server does not report a page's child composition
  (`childTypes` missing — as Cloud responds), attachments are now genuinely
  checked instead of being silently skipped.
- HTTP request retries honour the server's `Retry-After` header (429/503): the
  wait is taken from the header, capped at 60 seconds — matters for Confluence
  Cloud rate limits.
- POST requests are now retried on 429: the rate limiter rejects the request
  before processing, so duplicated side effects are impossible. POST is still
  never retried on network errors or 5xx.

## [2.16.0] — 2026-06-24

### Added

- Attachment change-source detection in merge. The tool determines which side
  changed an attachment (from the server version and the local raw-bytes hash, no
  download) and protects both sides: `upload merge` won't overwrite an attachment
  changed on the server; `download merge` won't clobber an attachment changed
  locally; changes on both sides are flagged as a conflict. Skips and conflicts go
  to the sync report. Previously an attachment moved in the command's direction and
  could silently overwrite the other side's change.
- `compare` shows the change source of each attachment: changed locally, changed
  on the server, or conflict (still download-free). Without a baseline yet, it
  falls back to the previous size comparison.

### Changed

- The force commands (`upload update` / `download update`) still overwrite an
  attachment changed on the opposite side (force means force), but now print a
  warning.

## [2.15.0] — 2026-06-24

### Fixed

- An attachment whose server name contains characters that are invalid in a
  local file name (e.g. `:`) is no longer duplicated on upload, nor renamed on
  the server. The tool now records the full server attachment name in the `.id`
  marker and matches/uploads by it — macro references (e.g. draw.io) stay valid.
  Previously the file was stored locally under a sanitised name and upload, using
  that name, failed to find the original and created a duplicate.

### Changed

- The page `.id` marker now also stores a per-attachment baseline (full server
  name, version, hash, size). The format is backward compatible: old markers are
  read as-is and the field is added on the next sync. This is groundwork for
  upcoming two-sided attachment change detection.

## [2.14.0] — 2026-06-24

### Added

- `compare` now detects attachment changes (by file name and size, without
  downloading) and prints an "Attachments changed" section: attachments whose
  size differs, or that exist only locally or only on the server. A same-size
  in-place edit is not detected — that is the cost of the download-free mode.

### Fixed

- `upload merge` and `upload update` no longer skip attachment changes when the
  page body itself is unchanged. Previously, with the page unchanged, attachment
  sync was not performed and an attachment-only edit (e.g. a draw.io diagram's
  source) was silently never uploaded.

## [2.13.1] — 2026-06-24

### Fixed

- Updating an extensionless attachment (e.g. a diagrams.net/draw.io source — the
  "twin" next to its `.png` preview) is no longer skipped on `upload`. The tool
  now explicitly sends the attachment's server media type, so Confluence does not
  re-infer it from the (missing) extension and reject the new version —
  previously the `.png` preview updated while its extensionless source stayed a
  version behind. New attachments get their type from the extension, falling back
  to `application/octet-stream`.

## [2.13.0] — 2026-06-10

### Fixed

- A concurrent server-side edit made between change analysis and the write can
  no longer be silently overwritten during `upload`: the write is now based on
  the version the tool observed during analysis, so a stale write is rejected
  by the server as a conflict (409) and recorded in the report. Bonus: a page
  update now costs one HTTP request less.
- A Confluence API failure during page-by-title search (401/403, 5xx, or a
  network error after the retry budget) is no longer interpreted as "page not
  found". Previously such a failure during a recursive upload could lead to an
  attempt to create a duplicate page; now the operation fails with an explicit
  error.
- Cancellation (Ctrl+C, MCP request cancellation) is no longer swallowed in
  version-history requests, historical page fetches, and attachment
  upload/update/delete — execution stops immediately instead of being masked
  as an "attachment failure" or an empty history.

### Changed

- Confluence API read errors (fetching a page, children, search, attachment
  download) are now reported the same way as write errors: with the HTTP status
  and a response-body snippet. In MCP mode such errors get precise codes
  (`PAGE_NOT_FOUND`, `AUTH_FAILED`, `RATE_LIMITED`) instead of the generic
  `NETWORK_ERROR`, and an authorization failure during a multi-tree upload
  aborts the whole batch, as it does for writes.

## [2.12.1] — 2026-06-07

### Fixed

- False "local edits" in compare and repeated re-upload of the same pages.
  Confluence canonicalises storage format on save: it assigns `ac:macro-id` to
  macros that lack one and drops empty `<ac:parameter ac:name="" />` elements, so
  content read back from the server no longer matched the local copy even without
  real edits — `compare` flagged pages as changed locally and
  `upload merge`/`update` created redundant versions. The normalizer now strips
  these artifacts for comparison (epoch 2), so such pages are no longer treated as
  changed. Additionally, the "local unchanged since sync" hash is now honoured when
  the marker and server versions match (previously always treated as a local edit)
  and in the upload no-op, as a safety net for other server-side transformations.

## [2.12.0] — 2026-06-07

### Added

- English versions of the documentation (`README.en.md`, `CHANGELOG.en.md`,
  `docs/ai-rules/README.en.md`, `docs/mcp/README.en.md`) with a language
  switcher. The Russian versions remain the primary ones.

### Changed

- Release archives now bundle both README versions — `README.md`
  (Russian) and `README.en.md` (English).
- More accurate double-edit conflict detection. At sync time a SHA-256 hash of
  the normalized content is stored in the `.id` marker (fields `h`/`ne`), so the
  next sync tells a real local edit apart from an mtime-only touch that leaves
  the content unchanged (editor re-save, pretty-print, copy, `touch`, VCS
  checkout) — these are no longer flagged as false conflicts. The hash is the
  primary signal; modification-time (mtime) comparison remains as a fallback
  (legacy markers, abnormal normalization). The marker format is extended
  backward-compatibly: old `.id` files keep reading unchanged, and the hash is
  added on the next sync.

## [2.11.0] — 2026-06-03

### Added

- Support for working with multiple spaces. The space a page belongs to is
  now stored locally (in the `.id` file body) and taken from the server,
  not only from the config/parameter. This makes it possible to keep
  trees from different spaces side by side and sync them — especially
  convenient via the MCP server.
- The `--multi-tree` flag (CLI) and the `multiTree` parameter (MCP tools
  `confluence_upload_update` / `confluence_upload_merge`): if `--source-dir`
  points to a directory with several trees (subfolders with `index.html`),
  each tree is processed independently, with its own space. Without the flag,
  such a directory raises a clear error instead of a silent failure.

### Changed

- The space for new child pages and for already-synced trees is determined
  by the tree on the server (by root/parent), not by the config value.
  The `spaceKey` parameter is usually not needed for an existing tree; if it
  is passed explicitly and contradicts the page's or parent's real space,
  the operation fails with an error (instead of being silently ignored).
- `confluence_get_page_content` returns the page's actual space.

### Fixed

- Protection against an unintended move of a subtree into another space: if a
  local folder maps to a page from a different space (it was moved manually or
  the `.id` file was edited), that page and its subtree are not updated
  or moved — they go into the report with an explanation.
- Quote escaping in page lookup by title: titles and space keys
  containing `"` no longer break the query.

## [2.10.1] — 2026-05-29

### Fixed

- Removed the noisy technical HTTP-request log introduced in 2.10.0: on every
  call to Confluence, four `System.Net.Http.HttpClient...` lines
  (Start/Sending/Received/End) were printed at the Information level — on all commands
  and regardless of `--report`. Now the per-request log is available only with
  `--verbose` (a single `[HTTP]` line per attempt).

### Changed

- Diagnostic timings (`[PROFILE]`) were moved from the Information level to Debug
  — in normal mode they no longer clutter the output, but are still visible with
  `--verbose`.

## [2.10.0] — 2026-05-29

### Fixed

- Version conflicts and other errors when uploading pages to the server are no
  longer silently lost. Previously a server write rejection (version conflict `409`,
  insufficient permissions, a deleted or invalid page) turned into "nothing
  happened": the page simply was not updated, and there was no trace of it
  in the report. Now a version conflict goes into the sync report as a conflict
  with a hint to run `download merge`; other write errors — as a skip with a
  reason. Authentication errors (`401`/`403`) abort the operation with an explicit
  error. On recursive upload, a single failed node no longer aborts the
  processing of the rest of the tree.
- Confluence API errors are classified explicitly: the CLI prints a clear
  message, and the MCP tools return a stable error code
  (`VERSION_CONFLICT`, `AUTH_FAILED`, `PAGE_NOT_FOUND`, `RATE_LIMITED`,
  `CONFLUENCE_API_ERROR`).

### Changed

- Long-running operations (syncing large trees, working in MCP mode)
  now react correctly to cancellation: requests to Confluence, page-tree
  traversal, and file operations stop on a cancellation signal instead of
  running to the end.

## [2.9.0] — 2026-05-27

### Fixed

- `upload merge` now correctly moves pages on the server when a folder is
  moved locally. Previously this only worked in `upload update`:
  `merge` ignored a parent change both for the root page (when
  `--source-dir` points directly at the moved folder) and for child
  pages during recursive traversal. Because of this, after a local move the
  only way to deliver the change to the server was a forced
  `upload update`, or a manual move in Confluence followed by
  `download merge`.
  New logic: if the local parent folder has an `.id` marker and its
  ID differs from the page's server parent, `upload merge` applies a
  structural move (`ancestors`). When there are no content or title changes,
  a "light" edit with only the new parent is sent; with a local content
  edit — a single call with the new content and new parent. If the
  content was also changed on the server since the last sync, the move
  is deferred and the page is marked as Skipped with a hint to run
  `download merge`, move the folder again, and repeat `upload merge`.

### Changed

- Agent instructions (`agent-instructions.md`): added the sections
  "Adding a new page to an already-synced hierarchy" and "Moving or renaming
  a page locally". They describe two typical scenarios where agents
  previously chose the wrong tool:
  - for a new page inside an already-synced subtree you should
    call `confluence_upload_merge` on the parent (or higher) with
    `recursive: true`, not `confluence_upload_create` (the latter requires
    a manual parent, silently fails on a duplicate title, and does not write
    the local `.id` marker on failure);
  - after a local folder move you should call
    `confluence_upload_merge`, not `confluence_download_merge` (the latter
    would return the folder to its server position, undoing the user's intent).
- The cheat sheet in the agent instructions was revised for the new scenarios.
- README: the "Command upload merge" section was extended with a description of
  the behaviour on local folder moves and creating new pages inside a
  synced hierarchy.

## [2.8.1] — 2026-05-26

### Changed

- Agent instructions (`agent-instructions.md`): added the section
  "Critical: outputDir / sourceDir must point to the right place" —
  an explicit description of the fact that `pageId` identifies only the page on the
  server, while the local path (`outputDir` / `sourceDir`) must always be passed.
  This eliminates the typical agent mistake of assuming the server itself
  would locate the page's position in the local tree by its ID.
- Clarified the descriptions of the `outputDir` parameter in the MCP tools: instead of
  "Output directory" it is now explicitly stated that this is the **parent**
  directory inside which the tool creates the page subfolder.
  The asymmetry with `sourceDir` (which points to the page folder
  itself) is emphasised.

## [2.8.0] — 2026-05-26

### Added

- The `compare` command now detects conflicts (pages changed simultaneously
  on the server and locally) and prints them in a separate "Conflicts" section — previously
  such two-sided edits were shown only in `download merge --report` and
  `upload merge --report`
- A new content normaliser based on `System.Xml.Linq` (`XmlContentNormalizer`) —
  it provides a more accurate canonical representation of XHTML during comparison,
  reducing the number of false positives compared with regex normalisation

### Changed

- Content normalisation during comparison was moved behind the `IContentNormalizer`
  interface and is wired in via DI — if needed, you can switch back to the previous
  regex normaliser (`RegexContentNormalizer`) by replacing one line in the configuration

## [2.7.2] — 2026-05-24

### Fixed

- The MCP tools falsely returned `OUT_OF_SANDBOX` for relative
  paths like `outputDir: "."` if the operator started the server with a
  `--root-dir` ending in a separator (e.g.
  `D:\…\Confluence\`). Root cause: `Path.GetFullPath` preserves
  the trailing separator in the input, while the same `GetFullPath` for a path
  built from `"."` removes it — the string-based sandbox check produced a
  mismatch by 1 character and flagged *the root itself* as outside.
  `PathSandbox.RootDir` is now normalised via
  `Path.TrimEndingDirectorySeparator(Path.GetFullPath(...))` with correct
  handling of drive roots (`C:\` stays `C:\`). Added
  regression tests for both `--root-dir` variants (with a trailing separator
  and without), as well as for `.` and `./subdir`.

## [2.7.1] — 2026-05-24

### Fixed

- The MCP tools `confluence_compare`, `confluence_get_page_content`,
  `confluence_download_update`, and `confluence_download_merge` did not work
  when called via an MCP client — the agent received a generic error
  "An error occurred invoking 'confluence_…'" with no details. The cause was
  a ModelContextProtocol SDK bug: nullable parameters (`pageId`, `pageTitle`)
  without an explicit `= null` in the signature were marked as "required" in the JSON Schema,
  and the SDK rejected calls in which one of the mutually exclusive parameters
  was not passed. Default values were added to all optional parameters.
- Errors arising at the SDK level (parameter binding, argument
  deserialisation) are no longer lost — a `CallToolFilter` was added that
  intercepts unhandled exceptions and returns a
  structured `{success, errorCode, error, logs}` envelope to the agent with
  a classified error code, instead of a useless generic string.

## [2.7.0] — 2026-05-24

MCP server polish following a real incident and feedback:
HTTP-layer reliability, error visibility, diagnostics, and helping the agent
resolve conflicts.

### Added

- The `confluence_ping` tool — lightweight read-only diagnostics of
  connectivity and credentials. It makes a single request to
  `/rest/api/user/current`, returning the base URL, the current user,
  latency, and sandbox settings. Works in `--read-only`. Purpose —
  the agent can first check "is the channel alive?" before
  launching heavy syncs.
- The `confluence_get_page_content` tool — fetch the storage-format
  XHTML of a page (the current version or a specific historical one via
  `version`). Built for the agent-assisted merge scenario: on a conflict
  the agent reads the local `index.html` with its own file tools,
  fetches the server content with this tool, does the diff/merge itself,
  and uploads the result via `confluence_upload_update`. A
  3-way merge is supported: the agent takes the base version for the merge from the local
  `.idPAGEID_VER` marker. Parameters: `normalize` (canonicalises
  the result via `StorageFormatNormalizer` for a semantic diff),
  `maxBytes` (default 256 KB, UTF-8-safe truncation). Works in
  `--read-only`.
- The agent guide `docs/mcp/agent-instructions.md` — a short
  English cheat sheet (what the server does, sandbox semantics, how
  to choose between tools, error codes, step-by-step scenarios for
  2-/3-way merge, and troubleshooting via `ping`). The file is embedded in
  the build and is automatically passed to the client via
  `McpServerOptions.ServerInstructions` → MCP `InitializeResult.Instructions`.
  Most clients (Claude Code, Claude Desktop, Cursor)
  blend these instructions into the system prompt without manual action
  by the operator. The same file is available in the repo — you can, if you wish,
  roll it into `CLAUDE.md`/`.cursorrules` so the agent knows the patterns
  even before connecting the server.
- Hints in the `summary` of the `confluence_compare`,
  `confluence_download_merge`, and `confluence_upload_merge` reports when
  conflicts / differences are present — they tell the agent that to resolve them it should
  call `confluence_get_page_content`.
- A new `NETWORK_ERROR` error code — an `HttpRequestException` without an
  HTTP status (DNS, TCP, SSL EOF, etc.). Previously such errors were lost
  in the generic `INTERNAL`.

### Changed

- All exceptions in the MCP tools are now returned to the agent with the
  **full `InnerException` chain**, joined with `→`. Previously the
  `error` field of the envelope held only `ex.Message`, and SSL errors
  looked like a useless "The SSL connection could not be
  established, see inner exception." — now the real cause is visible
  ("…→ Received an unexpected EOF…"). The chain is capped at 8 levels.
- On every MCP-tool error, the full exception (with stack trace)
  is written to stderr via `ILogger.LogError`. MCP hosts write stderr
  to their logs — the operator sees the details without having to restart
  the server. Defense-in-depth in case the envelope is lost somewhere along
  the way.
- The Confluence client's `HttpClient` was rebuilt on `SocketsHttpHandler`
  with `PooledConnectionLifetime=2min`, `PooledConnectionIdleTimeout=1min`,
  `ConnectTimeout=30s`. This addresses the root cause of the incident in which
  a long-lived MCP process, after a network break (VPN reconnect, NAT timeout),
  got stuck on stale TCP connections. Now the pool refreshes itself
  within ≤2 minutes.
- Introduced `RetryingHttpHandler` — automatic retry on transient
  errors. Up to 3 attempts with exponential backoff (250 ms / 500 ms
  / 1 s). Retries on: `HttpRequestException` without a status OR 408/429/502/503/504,
  `IOException`, `SocketException`. Does not retry on: 4xx (except 408/429),
  5xx (except 502/503/504), cancellation. Idempotent methods only
  (GET/HEAD/PUT/DELETE/OPTIONS); POST is not retried — so as not to
  duplicate create-page or upload-attachment. Each attempt is
  logged via `LogWarning` for visibility of retry storms in the
  MCP host's logs.

### Infrastructure

- GitHub Actions versions were updated to Node.js 24-compatible ones
  (`checkout@v6`, `setup-dotnet@v5`, `upload-artifact@v7`,
  `download-artifact@v8`, `softprops/action-gh-release@v3`,
  `dorny/test-reporter@v3`) — a response to GitHub's warning about the
  forced migration of the runner to Node.js 24 from June 2026.

## [2.6.0] — 2026-05-20

### Added

- The `mcp` subcommand — running the tool as an MCP (Model Context Protocol)
  stdio server, which lets AI agents (Claude Desktop, Cursor, Codex,
  and other MCP clients) perform Confluence sync operations.
  Six tools are exposed: `confluence_download_update`,
  `confluence_download_merge`, `confluence_upload_update`,
  `confluence_upload_create`, `confluence_upload_merge`, `confluence_compare`.
- The mandatory `--root-dir` parameter — a filesystem sandbox:
  all paths in the tools are resolved relative to it, and going outside
  the bounds is blocked (the `OUT_OF_SANDBOX` error).
- The `--read-only` flag — disables all upload tools, leaving
  download and compare. Returns `READ_ONLY_VIOLATION` on a write
  attempt.
- Documentation in [`docs/mcp/`](docs/mcp/README.en.md): example configs for
  Claude Desktop, Cursor, and Codex CLI.

### Changed

- The internal `IConsoleWriter` abstraction — all handlers and reports
  (`SyncReport`, `CompareReport`) write user output through it
  instead of direct `Console.WriteLine`. CLI behaviour did not change
  (the same default `StdConsoleWriter` implementation); this made it possible, in
  MCP mode, to buffer the output and attach it to the JSON result
  of the tool instead of cluttering stdout (reserved for JSON-RPC).

## [2.5.4] — 2026-04-30

### Fixed

- A crash (AccessViolationException) on macOS ARM64 when running
  `download merge` and `upload merge` with parallelism ≥4: the root cause is
  a .NET runtime bug (dotnet/runtime#123324) in single-file compressed applications
  on Apple Silicon. Fixes:
  - Disabled single-file compression for the osx-arm64 build
  - Completely removed the dependency on `System.Xml` and `WebUtility.HtmlDecode`
    in the content normaliser — replaced with regex-based canonicalisation
    using a static HTML-entity dictionary

## [2.5.2] — 2026-04-24

### Changed

- The `compare` command now uses parallel page-tree traversal
  (`Parallel.ForEachAsync` with `--max-parallelism`) — a speedup comparable
  to `download`/`upload` v2.5.0.
- The `compare` command skips the `/child/page` HTTP request for leaf
  pages if the server returned `childTypes.page.value = false` as part of the
  main GET. Saves ~N requests, where N is the number of leaves in the tree.

### Added

- A timing log `[PROFILE] Compare completed in <Ms>ms` at the
  Information level — the body of `CompareAsync` is wrapped in `try/finally`, the log
  is written on all exit paths (including errors).
- Regression tests for leaf-skip and parallel correctness of
  `CollectRemotePagesAsync` (collecting 10 children from a `ConcurrentDictionary`
  with no losses under parallelism).

## [2.5.1] — 2026-04-24

### Added

- Infrastructure timing logging at the Information level for the commands
  `download update`, `download merge`, `upload update`, `upload merge`,
  `upload create`: after the operation finishes, the line
  `[PROFILE] <Command> completed in <Ms>ms` is printed for estimating the duration of a
  sync under production load.
- HTTP tracing (`HttpTimingHandler`) at the Debug level: for each request
  to the Confluence REST API the method, URL, status, duration, and
  response size are recorded.
- SHA-256 timings of content and attachment comparisons at the Debug level
  (buffer size + milliseconds) in `DownloadService` and `UploadService`.

### Fixed

- `UploadCreateAsync`: the timing log `[PROFILE] UploadCreate completed`
  is now written on all exit paths (including the early `return` on
  `createResult == null`) — the body is wrapped in `try/finally`. Previously, on the
  failure path, the profiling data was lost.

## [2.5.0] — 2026-04-24

### Added

- Parallel page-tree traversal and attachment download in the `download` and `upload` commands: on real spaces (~130 pages) this cuts the sync time by 3–5×.
- The `--max-parallelism N` parameter (default 8) for regulating the level of parallelism of the `download` and `upload` commands. A value of `1` disables parallelism.

### Changed

- The `download` command skips redundant metadata HTTP requests (`child/page`, `child/attachment`) for leaf pages: Confluence returns the `childTypes.page.value` and `childTypes.attachment.value` flags as part of the main GET, and for leaves these calls are no longer made. Saves 100+ HTTP requests on a typical space.

## [2.4.0] — 2026-04-14

### Changed

- The download configuration now supports separate subsections `Download:Update` and `Download:Merge` with inheritance of shared parameters from `Download`
- The upload configuration supports shared parameters at the `Upload` level (e.g. `SourceDir`), inherited by the `Update`, `Create`, `Merge` subcommands
- The `config show` command displays all subcommands uniformly: `Download > Update`, `Download > Merge`, `Upload > Update`, `Upload > Create`, `Upload > Merge`, `Compare`

### Added

- The `Upload > Merge` section in the `config show` output
- The `DetectSource` parameter in the `Compare` section of the `config show` output

## [2.3.0] — 2026-04-11

### Added

- Storing the original Confluence page title in the `.id*` marker: on download
  the title is written into the marker file body and used on upload back
  to the server, which prevents pages from being renamed due to folder-name sanitisation.
- Automatic restoration of original titles on `upload update` and `upload merge`:
  if the folder was not renamed by the user, the saved title is used
  (with quotes, colons, and other characters not allowed in file names).
- Detection of a user folder rename: if the folder name does not match the sanitised
  form of the saved title, the folder is treated as renamed and the new name is used
  as the page title on upload to the server.
- Server-side protection against leaking sanitised names: when there is no saved title
  in the marker, the tool compares the local name with the server title and does not perform
  a rename if the folder name is the sanitised form of the server title.

### Fixed

- Inconsistent replacement of invalid characters in folder names: `SanitizeFileName` was replaced
  with a per-character replacement of each invalid character with `_` instead of `Split/Join`, which
  eliminates the asymmetric behaviour (e.g. `Модуль "Провайдеры"` previously produced
  `Модуль _Провайдеры`, now — `Модуль _Провайдеры_`).

## [2.2.1] — 2026-04-08

### Fixed

- False differences in `compare` for pages whose title ends with a dot (`.`)
  or a space: Windows automatically strips trailing dots and spaces from folder
  names, so the local name did not match the server title. Now
  `SanitizeFileName` explicitly trims trailing dots and spaces before creating the folder.

## [2.2] — 2026-04-07

### Fixed

- Page attachments were downloaded on every `download update` and `download merge`,
  even if they had not been changed on the server. A two-level check was added: first, comparing
  the local file size with `extensions.fileSize` from the API (a fast path without
  downloading), then — if the sizes differ — comparing the SHA-256 hashes of the downloaded and local file
  (handling the case where Confluence Server re-encodes a JPEG via ImageIO and returns a file
  with a size different from the one stated in the metadata).

## [2.1] — 2026-03-26

### Fixed

- False differences when comparing content due to insignificant differences in the formatting
  of the storage format. Before comparison, normalisation is performed: line endings (CRLF → LF),
  indentation between XML tags, attribute order, the format of self-closing tags (`<br/>` vs `<br />`),
  HTML entities (`&mdash;` vs `—`). When XML parsing is impossible, a fallback
  to line-ending normalisation is applied. Affects the commands `compare`, `download merge`,
  and `upload merge`.

## [2.0] — 2026-03-20

### Added

- The `download update` command — force-downloading pages from the server,
  overwriting local changes (making the local copy identical to the server one).
- The `download merge` command — smart downloading of pages from the server while preserving
  local changes: only pages changed on the server are overwritten;
  local edits are not wiped. Conflicts (edits on both sides) are detected and
  skipped with a warning.
- The `upload merge` command — uploading local pages to the server while preserving
  server changes: only pages changed locally are uploaded; server
  edits are not wiped. Conflicts (edits on both sides) are detected and skipped
  with a warning.
- The `--report` flag — when present, after `download`/`upload` finishes a summary is printed of
  pages requiring manual handling (two-sided-edit conflicts, skipped pages).
- Detection of two-sided-edit conflicts: if the server version of a page is newer than the marker
  and at the same time the local file was changed after the last sync — the page
  is marked as conflicting and is not overwritten in either direction.

### Changed

- **BREAKING:** The `download` command was split into the subcommands `download update`
  and `download merge`. The former `download` call must be replaced with `download update`.
- **BREAKING:** The `upload update` command was simplified: page moving is done
  always automatically.
- Attachment updates on `upload update` and `upload merge`: instead of deleting and
  re-creating, the attachment is updated via Confluence versioning
  (a new version of the file). Unchanged attachments are skipped (comparison by file size
  and SHA-256 hash).

### Removed

- **BREAKING:** The `--overwrite-strategy` parameter (replaced by splitting `download`
  into `update`/`merge`).
- **BREAKING:** The `--on-error` parameter (in `update` mode, errors abort execution).
- **BREAKING:** The `--move-pages` parameter (page moving is done automatically
  in `update`).

### Fixed

- The "XSRF check failed" error when uploading attachments: the mandatory header
  `X-Atlassian-Token: nocheck` was added to attachment requests.
- The "MethodNotAllowed" error when deleting attachments: the URL endpoint was fixed.

## [1.1] — 2026-03-17

### Added

- The page version in the `.id<pageId>_<version>` marker: on `download` and `upload update`
  the marker is automatically created or updated, reflecting the current id and version number
  of the page on the server. Backward compatibility with the old `.id<pageId>` format is preserved.
- Skipping unchanged pages on `upload update`: before sending to the server the
  title, content, and parent page are compared — if nothing has changed, the update
  is skipped, which prevents creating redundant versions in Confluence.
- Determining the source of content changes by marker version in the `compare` command:
  if the marker version matches the server's — the change is local (high confidence);
  if the server version is newer — the change is on the server (high confidence).

## [1.0] — 2026-03-15

### Added

- Change-source detection in the `compare` command: for each detected difference
  (rename, move, content change), the likely source is printed — server
  or local copy — with the confidence level indicated (low / medium / high).
- The `--detect-source` flag in the `compare` command: enables analysis of the Confluence
  version history to improve the accuracy of determining the source of renames and moves.
- Fetching a page's version history via the Confluence REST API
  (`GetPageVersionsAsync`, `GetPageAtVersionAsync`).
- The `ChangeSourceAnalyzer` service — determining the source of differences by modification dates
  and version history; caching of API requests.
- The server page's last modification date (`version.when`) is passed into the snapshots
  and used during comparison.
- The modification dates of local folders and `index.html` files are passed into the snapshots
  and used during comparison.

### Fixed

- The `upload update` command did not detect a move of the root page (specified
  via `--source-dir`) to a different parent. Now, when there is an `.id` marker
  in the parent directory and `--move-pages` is enabled, the root page is correctly
  moved; when `--move-pages` is disabled — an error is printed.
- The `compare` command did not detect a change of the parent page for the root
  comparison page. An explicit check of the parent directory's `.id` marker was added
  against the server `ParentId` with change-source analysis.

## 2026-03-13

### Added

- The `config show` command — displaying the effective configuration with the source of
  each value (`[CLI]`, `[ENV]`, `[FILE]`, `[DEFAULT]`).
- Multi-layered configuration: loading parameters from a JSON file (`--config`), environment
  variables (the `CONFLUENCE_EXPORTER__` prefix), and command-line arguments with the priority
  CLI > ENV > FILE > DEFAULT.
- Migration to `Microsoft.Extensions.Hosting` with a two-phase startup via `BootstrapParser`
  and `CommandDispatcher`.
- Section-based options for each command (`Download`, `Upload:Update`, `Upload:Create`,
  `Compare`) via `Microsoft.Extensions.Options`.
- Shared (recursive) parameters at the root-command level, inherited by all subcommands.

### Changed

- Path normalisation: quotes and escaped spaces in the `--output-dir` and
  `--source-dir` arguments are handled correctly via `PathNormalizer`.

## 2026-03-10

### Changed

- Test framework upgrade: migration from xUnit 2 to xUnit v3.

## 2026-03-07

### Added

- The `--move-pages` parameter in the `upload update` command: moving a page under a new
  parent if its position in the local tree differs from the server one.

## 2026-03-04

### Added

- Command error handling: clear error messages for the user
  (missing required parameters, non-existent directories, etc.).
- Validation of local directories before performing operations (`ValidateSourceDirectory`).

### Fixed

- Correct determination of a page's folder name when the path has a trailing separator.

## 2026-02-28

### Added

- The `ConfluencePageExporter.Tests` unit test project (xUnit, Moq, FluentAssertions).
- Mocks for the API client and the HTTP handler (`ApiClientMockFactory`, `StubHttpMessageHandler`).

## 2026-02-27

### Added

- Initial implementation of the tool.
- The `download` command — exporting a page (or page subtree) from Confluence to disk.
- The `upload update` command — updating existing Confluence pages from local
  content.
- The `upload create` command — creating new pages in Confluence from the local structure.
- The `compare` command — comparing the Confluence page tree with a local snapshot: detecting
  added, deleted, renamed/moved pages and content changes.
- The local storage format: a folder per page, `index.html`, an `.id<pageId>` marker,
  attachments as files.
- Matching pages by `.id<pageId>` markers and a fallback by titles
  (`--match-by-title`).
- Recursive page-tree processing (`--recursive`).
- Authentication: `--auth-type onprem` and `--auth-type cloud`.
- Verbose logging (`--verbose`).
- Dry-run mode (`--dry-run`).
- Selecting a page by `--page-id` or `--page-title`.
- Service architecture: `DownloadService`, `UploadService`, `CompareService`,
  `LocalStorageHelper`, `HttpClientConfluenceApiClient`.
