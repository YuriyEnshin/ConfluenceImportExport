# Confluence Page Exporter MCP server

[Русский](README.md) | **English**

Confluence Page Exporter can run as a
[Model Context Protocol (MCP)](https://modelcontextprotocol.io) server — this
lets AI agents (Claude Desktop, Cursor, Codex CLI, Continue, etc.)
synchronise Confluence pages with a local tree the same
way a human would from the CLI.

## Running

```bash
ConfluencePageExporter mcp --root-dir <path> [--read-only]
```

Parameters:

| Parameter | Purpose |
|---|---|
| `--root-dir <path>` | **Required.** The sandbox root folder. All paths the agent passes to the tools are resolved relative to it. Absolute paths are accepted only if they lie inside `root-dir`. |
| `--read-only` | Optional. Blocks all upload tools (`download`/`compare` remain available). Useful when the agent needs read-only access. |

The Confluence connection parameters (`BaseUrl`, `Username`, `Token`,
`SpaceKey`, `AuthType`) are set via the same mechanisms as for the CLI:

1. Environment variables with the `CONFLUENCE_EXPORTER__` prefix —
   the **recommended way** for MCP, set in the MCP client's config.
2. A JSON config via `--config <path>` (the same format as for the CLI).

The authentication parameters are **never passed into the tools from
the agent** — this is intentional, so the token does not reach the LLM's
context window.

## Tools

| Name | Purpose | CLI equivalent |
|---|---|---|
| `confluence_download_update` | Download pages, force-overwriting local files | `download update` |
| `confluence_download_merge` | Download only server changes, preserving local edits | `download merge` |
| `confluence_upload_update` | Upload local pages, overwriting server changes | `upload update` |
| `confluence_upload_create` | Create new pages in Confluence from a local folder | `upload create` |
| `confluence_upload_merge` | Upload only local changes, preserving server edits | `upload merge` |
| `confluence_compare` | Compare the Confluence tree with the local copy | `compare` |
| `confluence_ping` | **Diagnostics.** Check connectivity and credentials with one lightweight request; return the base URL, current user, latency, and sandbox settings. Works in `--read-only`. | — |
| `confluence_get_page_content` | **Merge helper.** Return the storage-format (XHTML) of a given page — the current version or a specific historical one. Built for the "conflict → diff → merge" scenario: the agent reads the local `index.html` with its own file tools, calls this tool for the server version, does the diff, and assembles the merged variant. Works in `--read-only`. | — |

All tools return a JSON envelope of a single format:

**Success:**
```json
{
  "success": true,
  "summary": "Download merge completed in C:\\confluence-mirror\\DOCS; 0 conflict(s).",
  "report": { /* SyncReport or CompareReport, if report=true */ },
  "logs": [ "Download merge: page ID '123'...", "..." ]
}
```

**Error:**
```json
{
  "success": false,
  "errorCode": "OUT_OF_SANDBOX",
  "error": "Path '../etc' resolves to '...' which is outside the sandbox root '...'.",
  "logs": [ "..." ]
}
```

### Error codes

| `errorCode` | When it occurs |
|---|---|
| `INVALID_ARGS` | An invalid combination of parameters (e.g. both `pageId` and `pageTitle` set); `spaceKey` missing from both the config and the arguments. |
| `OUT_OF_SANDBOX` | The passed path, after normalisation, turned out to be outside `--root-dir`. |
| `READ_ONLY_VIOLATION` | An attempt to call an upload tool on a server with the `--read-only` flag. |
| `AUTH_FAILED` | 401/403 from Confluence or an `UnauthorizedAccessException` on the filesystem. |
| `NETWORK_ERROR` | An `HttpRequestException` without an HTTP status (DNS, TCP, SSL EOF, etc.). The MCP server already retries up to three times with exponential backoff on idempotent requests (GET/PUT/DELETE); if the error reached the agent — all attempts failed. Check the network/VPN via `confluence_ping`. |
| `PAGE_NOT_FOUND` | 404 from Confluence or unable to resolve the page by `pageId`/`pageTitle`. |
| `DIRECTORY_NOT_FOUND`, `FILE_NOT_FOUND` | The local path does not exist. |
| `INVALID_STATE`, `IO_ERROR`, `INTERNAL` | Other execution errors. |

## Sandbox

The sandbox (`--root-dir`) is a **hard security invariant**:
an agent connected to the server cannot:

- pass a tool a path outside `--root-dir`,
- override `--root-dir` via a config file or an environment
  variable,
- unblock the upload tools if the server was started with `--read-only`.

The `--root-dir` and `--read-only` parameters are accepted **only** from
the command-line arguments of the `mcp` command (not from IConfiguration), which
guarantees they cannot be changed via the environment or a config file.

## Agent instructions

The [`agent-instructions.md`](agent-instructions.md) file is a short guide **in
English, for the agent** (not for the operator): what the server does, how
the sandbox works, how to choose between the tools, and (most importantly)
the full 2-way and 3-way merge scenarios on conflicts.

This file is delivered to the agent **in two ways**:

1. **Automatically.** On startup, the MCP server embeds it into the
   `InitializeResult.Instructions` field of the MCP protocol. Most clients
   (Claude Code, Claude Desktop, Cursor) blend this into the agent's system
   prompt — no manual action is required. The file
   is updated together with the server release.
2. **Manually (optional).** If you want the agent to know the rules
   *before* connecting the server (or together with other project context) —
   copy the content into `CLAUDE.md`, `.cursorrules`, a ChatGPT
   system prompt, etc. This is convenient, for example, in the commit instructions
   of the repository where the Confluence-folder mirror lives.

Both ways are compatible: duplicated agent instructions do no harm,
and in case of a mismatch (e.g. you rolled an old file into
the rules, but the server is already new) — ServerInstructions wins, since
it arrives closer to the moment of use.

## Client config examples

- [Claude Desktop](claude-desktop.json) — `claude_desktop_config.json`
- [Cursor](cursor.json) — `.cursor/mcp.json` in the project
- [Codex CLI](codex.toml) — `~/.codex/config.toml`

In all examples, substitute the Confluence token for
`<API_TOKEN>`.

## What the agent does after getting the result

Since the MCP tools perform a physical write to disk (into
`--root-dir`) or to Confluence, the agent **can and should** work with the
resulting tree using its own filesystem tools (Read, Grep, Bash, etc.).
The MCP server deliberately does not duplicate
this functionality — it is responsible only for synchronisation.

## Scenario: resolving a conflict with the agent's help

`download_merge` and `upload_merge` report conflicts (edits on both
sides) as `ConflictPages`, but do not resolve them automatically.
To ask the agent for help:

1. **Detection.** Run `confluence_download_merge` or
   `confluence_compare` — the `summary` / `ConflictPages` will tell you which
   pages were touched on both sides.
2. **Server content.** For each conflicting page, the agent calls
   `confluence_get_page_content` with `pageId` (optionally
   `normalize=true` — so the diff is "semantic", without noise from
   attribute order and whitespace).
3. **3-way merge (optional).** The `.idPAGEID_VER` file stores the
   version of the last sync. The agent can call
   `confluence_get_page_content` a second time with `version=N` — it will get
   the "common base" for a three-way merge (local vs server-current
   vs server-at-last-sync).
4. **Local content.** The agent reads the local `index.html` with its own
   tools (`Read`).
5. **Diff and merge.** The agent reconciles the edits itself and overwrites
   `index.html` with its own `Edit` tool.
6. **Upload.** `confluence_upload_update` (or `confluence_upload_merge`,
   if you want one more pass of protective logic) sends the
   result to the server.

For large pages (>256 KB of storage XML) `confluence_get_page_content`
returns the content marked `truncated=true` with a `fullSize` field. In that
case it is easier to do `confluence_download_update` and work with the file
from disk.
