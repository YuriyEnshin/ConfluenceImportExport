# Confluence Page Sync v2.16.0

[Русский](README.md) | **English**

A command-line tool for synchronising Confluence pages with a local folder structure.

The tool supports:

- downloading pages from Confluence to disk with a forced overwrite (`download update`) or smart merge (`download merge`)
- uploading local pages back to Confluence with a forced overwrite (`upload update`), smart merge (`upload merge`), or creating new ones (`upload create`)
- comparing the Confluence page tree with a local snapshot (`compare`)
- viewing the effective configuration with the source of each value (`config show`)

## Key features

- Git-like sync model: `update` (force) and `merge` (smart)
- Conflict detection: if a page was changed both locally and on the server, the conflict is detected, no overwrite is performed, and the user gets a warning
- `--report` — a summary of pages requiring manual resolution (conflicts, deleted pages)
- Selecting a page by `--page-id` or `--page-title`
- Optional recursive processing (`--recursive`)
- Working with multiple spaces: a page's space is stored in its marker and taken from the server; trees from different spaces can sit side by side and be synced together (`--multi-tree` for upload). An unintended move of a subtree into a foreign space is rejected and recorded in the report
- Local snapshot format:
  - one folder per page (folder name = page title, sanitised for the filesystem)
  - an `index.html` file with the page content in storage representation
  - attachments as separate files
  - a marker file `.id<pageId>_<version>` (its body is JSON with the original title and space key) for stable page identification and version tracking
- Authentication modes: `--auth-type onprem` and `--auth-type cloud`; by default the type is auto-detected from `--base-url` (`*.atlassian.net` hosts → `cloud`)
- Confluence Cloud support (REST API v2) — read-only for now: `download update`/`download merge`, `compare`, and the `confluence_ping` / `confluence_get_page_content` MCP tools. Write operations (upload, attachments) on Cloud fail with a clear error and arrive in upcoming releases
- Multi-layered configuration with priority: CLI > environment variables > file > default value
- Global `--verbose` flag for detailed (debug-level) output
- Dry-run support where applicable

## Local storage structure

On export (`download update`/`download merge`) pages are saved into a folder hierarchy inside `--output-dir`.
Each page folder contains the content, the identity marker, and attachments.

```text
<output-dir>/
  Root Page/
    index.html
    .id12345_7
    image.png
    spec.pdf
    Child Page A/
      index.html
      .id23456_3
    Child Page B/
      index.html
      .id34567_1
```

Rules:

- the page folder name = the page title in Confluence (each invalid character is replaced with `_`); the original title is preserved in the `.id*` marker and restored on upload to the server; renaming a folder is interpreted as the intent to rename the page
- `index.html` contains `body.storage.value`
- the `.id<pageId>_<version>` file is used for stable matching during sync and comparison; the `_<version>` suffix reflects the page version number on the server at the moment of the last sync; the marker's last write time (`LastWriteTimeUtc`) is used as the reference point for conflict detection; the file body stores the original Confluence page title for restoration on upload
- every file other than `index.html` and `.id*` is treated as a page attachment

### Mirror portability across operating systems

Folder-name sanitisation follows the rules of the filesystem on which the mirror is created: on Windows the characters `< > : " / \ | ? *` and control characters are replaced, on Linux/macOS — only `/` and `\0`. This means the **mirror is bound to the OS on which it was created**:

- the title `Модуль "Провайдеры"` produces the folder `Модуль _Провайдеры_` on Windows, but `Модуль "Провайдеры"` on Linux (such a folder name cannot be created on Windows);
- copying a mirror directly between operating systems (via cloud folders, `scp`/`rsync`, network shares) may lead to invalid names or to the tool no longer recognising already-synced pages.

The recommended approach when switching machines is to re-export the mirror from the Confluence server via `download update` or `download merge`, rather than copying the existing local folder.

## Rule for AI assistants

The [`docs/ai-rules/`](docs/ai-rules/) folder contains [`local-mirror-format.mdc`](docs/ai-rules/local-mirror-format.mdc) — a description of the structure and format of the local Confluence mirror, which you can attach to your AI assistant (Cursor, Claude Code, Continue, Aider, Windsurf, etc.) so that it correctly understands the folder hierarchy, the `index.html` format (Confluence Storage Format), the `.id*` markers, and attachments when working with the exported page tree.

Setup instructions for the various tools are in [`docs/ai-rules/README.en.md`](docs/ai-rules/README.en.md).

## MCP server for AI agents

The tool can be started as an [MCP](https://modelcontextprotocol.io) server
over stdio — this gives AI agents (Claude Desktop, Cursor, Codex, etc.)
the ability to perform six sync operations
(`download update`/`merge`, `upload update`/`create`/`merge`, `compare`)
straight from the chat, without switching to a terminal, plus two helper tools
for self-diagnostics and agent-assisted merge:

- `confluence_ping` — a lightweight connectivity and credentials check
  (base URL, current user, latency, sandbox settings);
  available even in `--read-only`.
- `confluence_get_page_content` — fetch the storage-format XHTML of a
  page (current or historical version) for the "conflict → diff → merge"
  scenario, where the agent reads `index.html` locally with its own
  file tools and reconciles the edits. Supports 2-way and
  3-way merge (via the `version` from the local `.idPAGEID_VER` marker).

```bash
ConfluencePageExporter mcp --root-dir <path> [--read-only]
```

- `--root-dir` — a mandatory sandbox: the agent cannot write outside it.
- `--read-only` — blocks the upload tools (download/compare/ping/get_page_content remain).
- Confluence connection parameters (BaseUrl/Username/Token/SpaceKey/AuthType)
  are set via the `CONFLUENCE_EXPORTER__*` env vars or a JSON config —
  they never reach the LLM context window.
- The server automatically passes the agent a short usage guide
  via the MCP `InitializeResult.Instructions` — most clients
  blend this into the system prompt without manual steps.

A detailed description of the tools, the result format, error codes,
the 2-/3-way merge scenarios, the agent guide, and ready-made configs for
Claude Desktop / Cursor / Codex are in [`docs/mcp/README.en.md`](docs/mcp/README.en.md).

## Installation

Pre-built binaries are published on [GitHub Releases](https://github.com/YuriyEnshin/ConfluenceImportExport/releases) as self-contained single-file archives — no need to install the .NET Runtime.

| Platform      | Archive                                                   |
|---------------|-----------------------------------------------------------|
| Windows x64   | `ConfluencePageExporter-v<version>-win-x64.zip`           |
| Linux x64     | `ConfluencePageExporter-v<version>-linux-x64.tar.gz`      |
| macOS arm64   | `ConfluencePageExporter-v<version>-osx-arm64.tar.gz`      |

After extracting the archive you get a single executable (`ConfluencePageExporter.exe` or `ConfluencePageExporter`), `README.md`, `README.en.md`, and `LICENSE`. On macOS/Linux you will need `chmod +x ConfluencePageExporter`.

## Building from source

```bash
dotnet build
```

Publishing a self-contained single-file build yourself:

```bash
dotnet publish src/ConfluencePageExporter -c Release -r <RID> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

where `<RID>` is one of `win-x64`, `linux-x64`, `linux-arm64`, `osx-arm64`, `osx-x64`.

## Configuration

The tool uses the standard `Microsoft.Extensions.Configuration` pipeline. Parameters are read from several sources by priority (the last one wins):

1. **JSON file** — by default `confluence-exporter.json` in the current directory; the path can be set explicitly via `--config <path>`.
2. **Environment variables** — with the `CONFLUENCE_EXPORTER__` prefix, a double underscore separates sections (e.g. `CONFLUENCE_EXPORTER__GLOBAL__BASEURL`).
3. **Command-line arguments** — explicitly given CLI arguments have the highest priority.

### Global parameters

Specified before the command name:

- `--config <path>` — path to the JSON configuration file
- `--verbose` — enable detailed (debug-level) log output
- `--report` — print a summary of pages requiring manual handling after the command finishes
- `--max-parallelism N` — the maximum number of concurrent operations when traversing the page tree in `download`/`upload` (default `8`; `1` disables parallelism)

### Example `confluence-exporter.json`

```json
{
  "Global": {
    "BaseUrl": "https://wiki.example.com",
    "Username": "user@example.com",
    "Token": "token-or-password",
    "SpaceKey": "DOCS",
    "AuthType": "onprem",
    "DryRun": false,
    "Recursive": true,
    "Report": false,
    "MaxParallelism": 8
  },
  "Download": {
    "PageId": "12345",
    "OutputDir": "./export",
    "Merge": {
      "OutputDir": "./export-merge"
    }
  },
  "Upload": {
    "SourceDir": "./export",
    "Update": {
      "PageId": "67890"
    },
    "Create": {
      "ParentTitle": "Architecture"
    },
    "Merge": {
      "PageTitle": "MyPage"
    }
  },
  "Compare": {
    "OutputDir": "./export",
    "MatchByTitle": true,
    "DetectSource": false
  }
}
```

### Parameter inheritance

The configuration supports a two-level model: shared command parameters are inherited by subcommands and can be overridden at the subcommand level.

The value resolution chain (from highest to lowest priority):

1. **Subcommand section** — e.g. `Download:Update:OutputDir`
2. **Command section** — e.g. `Download:OutputDir`
3. **Global** — for the `Recursive` parameter (a fallback in the handler code)
4. **Default value** — `false` / `null`

Example: given the JSON `"Download": { "PageId": "12345", "OutputDir": "./export", "Merge": { "OutputDir": "./export-merge" } }`, then:
- `download update` gets `PageId = 12345`, `OutputDir = ./export` (inherited from `Download`)
- `download merge` gets `PageId = 12345` (inherited), `OutputDir = ./export-merge` (overridden)

### Environment variables

Environment variables are named in the format `CONFLUENCE_EXPORTER__<Section>__<Parameter>` (all uppercase):

```bash
export CONFLUENCE_EXPORTER__GLOBAL__BASEURL=https://wiki.example.com
export CONFLUENCE_EXPORTER__GLOBAL__USERNAME=user@example.com
export CONFLUENCE_EXPORTER__GLOBAL__TOKEN=secret
export CONFLUENCE_EXPORTER__DOWNLOAD__OUTPUTDIR=./export
export CONFLUENCE_EXPORTER__DOWNLOAD__UPDATE__PAGEID=12345
export CONFLUENCE_EXPORTER__UPLOAD__SOURCEDIR=./export
```

## Invocation format

```text
ConfluencePageExporter [global parameters] <command subcommand> [command parameters]
```

## Command overview

```text
ConfluencePageExporter download update ...    # forced download (server → local)
ConfluencePageExporter download merge ...     # smart download preserving local edits
ConfluencePageExporter upload update ...      # forced upload (local → server)
ConfluencePageExporter upload merge ...       # smart upload preserving server edits
ConfluencePageExporter upload create ...      # create new pages
ConfluencePageExporter compare ...            # comparison and report
ConfluencePageExporter config show            # display configuration
```

## Sync model

The tool uses a git-like model with two modes:

| Mode | Description |
|-------|----------|
| **update** | Forced sync. The source is treated as the reference; the target side is overwritten. Local/server edits on the target side will be lost. |
| **merge** | Smart sync. Only pages changed on the source side are overwritten. Edits on the target side are preserved. Pages with a conflict (edits on both sides) are skipped with a warning. |

### Typical usage scenarios

```bash
# Fully download the server version, wiping local changes
ConfluencePageExporter download update --page-id 12345 --output-dir ./export --recursive

# Download only server updates, preserving local edits
ConfluencePageExporter download merge --page-id 12345 --output-dir ./export --recursive

# Upload local changes to the server, wiping server edits
ConfluencePageExporter upload update --source-dir ./export/MyPage --recursive

# Upload only local updates, preserving server edits
ConfluencePageExporter upload merge --source-dir ./export/MyPage --recursive

# Two-way sync (the newest changes from both sides are preserved)
ConfluencePageExporter download merge --page-id 12345 --output-dir ./export --recursive --report
ConfluencePageExporter upload merge --source-dir ./export/MyPage --recursive --report
```

### Conflict detection

In `merge` mode the tool uses the `.id<pageId>_<version>` marker to detect conflicts:

- **syncTimeUtc** = the marker file's last write time (the moment of the last sync)
- **serverChanged** = the server version is newer than the version in the marker
- **localChanged** = `index.html` was modified after `syncTimeUtc`
- If both flags are `true` → **conflict**: the page is not overwritten in either direction, and a warning is printed

With the `--report` flag, a summary of all pages requiring manual resolution is printed after the command finishes.

## Command download update

Force-downloads a Confluence page (or subtree) to the local disk. Differing pages are overwritten with the server versions. Local edits will be lost.

### download update parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--page-id` or `--page-title` (exactly one must be specified)
- `--output-dir` (required)
- `--recursive` (optional)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)
- `--dry-run` (optional)
- `--report` (optional)

### download update example

```bash
ConfluencePageExporter download update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Command download merge

Downloads pages from the server, overwriting only those that are newer on the server. Local edits are preserved. Conflicts (edits on both sides) are skipped with a warning.

### download merge parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--page-id` or `--page-title` (exactly one must be specified)
- `--output-dir` (required)
- `--recursive` (optional)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)
- `--dry-run` (optional)
- `--report` (optional)

### download merge example

```bash
ConfluencePageExporter --report download merge \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Command upload update

Force-uploads local pages to the server. Differing pages are overwritten with the local versions. Server edits will be lost. Moving pages when the parent differs is done automatically.

### upload update parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--source-dir` (required)
- `--page-id` or `--page-title` (optional, explicit root page)
- `--recursive` (optional)
- `--multi-tree` (optional; `--source-dir` points to a directory with several trees — each is processed with its own space; incompatible with `--page-id`/`--page-title`)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)
- `--dry-run` (optional)
- `--report` (optional)

### Root page resolution priority

1. Explicitly given `--page-id` / `--page-title`
2. The local `.id<pageId>_<version>` marker file in `source-dir`
3. The `source-dir` folder name as the page title

If the root page cannot be found, the command fails with an error.

### Skipping unchanged pages

Before sending an update, the tool compares the local content with the server's. If the title, content, and parent page have not changed, the update is skipped — this prevents creating redundant versions on the server.

### Updating attachments

Attachments are updated via Confluence versioning (creating a new version of the file). Before uploading, the tool checks whether the file has changed (by size and SHA-256 hash). Unchanged attachments are skipped.

### upload update example

```bash
ConfluencePageExporter upload update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --source-dir ./export/MyPage \
  --recursive
```

## Command upload merge

Uploads only locally changed pages to the server. Server edits are preserved. Conflicts (edits on both sides) are skipped with a warning.

Additionally, `upload merge` recognises structural changes in the local tree:

- **Local folder move** (the new parent folder has its own `.id` marker) — the page is moved on the server (`ancestors`) in a single API call.
- **A new folder inside an already-synced subtree** — it is created as a page on the server with the correct parent and the local `.id` marker is written automatically. There is no need to use `upload create` for single new pages inside an existing hierarchy.

If the same page's content was also changed on the server, the structural move is deferred: the page goes into the Skipped section with a hint to run `download merge`, move the folder again, and repeat `upload merge`.

### upload merge parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--source-dir` (required)
- `--page-id` or `--page-title` (optional, explicit root page)
- `--recursive` (optional)
- `--multi-tree` (optional; `--source-dir` points to a directory with several trees — each is processed with its own space; incompatible with `--page-id`/`--page-title`)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)
- `--dry-run` (optional)
- `--report` (optional)

### upload merge example

```bash
ConfluencePageExporter --report upload merge \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --source-dir ./export/MyPage \
  --recursive
```

## Command upload create

Creates new Confluence pages from local content.

### upload create parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--source-dir` (required)
- `--parent-id` or `--parent-title` (optional)
- `--recursive` (optional)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)
- `--dry-run` (optional)

### upload create example

```bash
ConfluencePageExporter upload create \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --parent-id 67890 \
  --source-dir ./export/NewPage \
  --recursive
```

## Command compare

Compares the Confluence page tree with a local snapshot and prints a report.
For each detected difference, the likely source of the change (server or local) is determined based on modification date comparison.
With `--detect-source`, the Confluence version history is additionally analysed to improve the accuracy of determining the source of renames and moves.

### compare parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--page-id` or `--page-title` (exactly one must be specified)
- `--output-dir` (required)
- `--recursive` (optional)
- `--match-by-title` (optional)
- `--detect-source` (optional) — analyse version history to determine the source of renames and moves (extra API calls)
- `--auth-type onprem|cloud` (optional, default: auto-detected from `--base-url`)

### Matching strategy

- by default: matching local pages by `.id<pageId>_<version>`
- with `--match-by-title`: if `.id` is missing, a fallback matching by folder titles/path is used

### Change source detection

For each detected difference, the tool tries to determine where the change happened — locally or on the server. Two levels of heuristics are used:

1. **Marker version comparison** (for content, if `.id<pageId>_<version>` is available) — if the marker version matches the server's, the change is local; if the server version is newer, the change is on the server. Confidence: high.

2. **Date comparison** (always, as a fallback) — compares the server page's last modification date (`version.when`) with the modification date of the local folder (rename/move) or the `index.html` file (content). Confidence: medium.

3. **Version history analysis** (with `--detect-source`) — for renames, it looks for the former title in the page's version history; for moves, it looks for the former parent in the ancestors of historical versions. Confidence: high.

### compare example

```bash
ConfluencePageExporter compare \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --match-by-title \
  --detect-source \
  --output-dir ./export
```

Example output:

```text
Compare report
==============
Added in Confluence: 1
  + [55555] New Page (Root/New Page)
Deleted in Confluence: 0
Renamed/moved: 1
  ~ [12345] New Title | local: Root/Old Title -> confluence: Root/New Title
    Rename: SERVER (high) — title 'Old Title' found in server version 3
Content changed: 1
  * [23456] Some Page (Root/Some Page)
    Source: LOCAL (medium) — local file changed (2026-03-12) later than the server (2026-03-10)
```

## Command config show

Prints the current effective configuration with the source of each value.

Possible sources:

- `[CLI]` — set by a command-line argument
- `[ENV]` — set by an environment variable
- `[FILE]` — set in the JSON configuration file
- `[DEFAULT]` — the default value

### config show example

```bash
ConfluencePageExporter config show
```

Example output:

```text
Effective configuration
=======================

Global:
  BaseUrl                      = https://wiki.example.com            [FILE]
  Username                     = user@example.com                    [FILE]
  Token                        = to***en                             [FILE]
  SpaceKey                     = DOCS                                [FILE]
  AuthType                     = onprem                              [DEFAULT]
  Verbose                      = False                               [DEFAULT]
  DryRun                       = False                               [DEFAULT]
  Recursive                    = True                                [FILE]
  Report                       = False                               [DEFAULT]
```

## Verbose logging

To enable debug-level output, use the global `--verbose` flag:

```bash
ConfluencePageExporter --verbose download update \
  --page-id 12345 \
  --output-dir ./export
```

## Migration from v1.x

Version 2.0 introduced breaking changes to the command structure:

| v1.x | v2.0 | Description |
|------|------|----------|
| `download` | `download update` | Forced download |
| — | `download merge` | Smart download (new command) |
| `upload update --on-error abort` | `upload update` | The `--on-error` parameter was removed; on error, execution aborts |
| `upload update --move-pages` | `upload update` | The `--move-pages` parameter was removed; moving is done automatically |
| `download --overwrite-strategy overwrite` | `download update` | The `--overwrite-strategy` parameter was removed |
| — | `upload merge` | Smart upload (new command) |
| — | `--report` | Report on pages with conflicts (new global flag) |
