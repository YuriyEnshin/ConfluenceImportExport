# Confluence Page Exporter — agent instructions

You are connected to a Confluence Page Exporter MCP server. This document tells you how to use it well. The server's job is to **synchronise pages between Confluence and a local directory tree**. It does not browse pages, comment, or do anything else — for reading or editing local files use your own filesystem tools.

These same instructions are also published as `docs/mcp/agent-instructions.md` in the project repo.

---

## How the server is configured

- **Sandbox root.** The server was started with `--root-dir <path>`. Every path you pass to a tool is resolved relative to that root; absolute paths are accepted only when they lie inside it. Outside paths fail with `OUT_OF_SANDBOX`. **You cannot widen this sandbox** — it is set at server startup and ignores env vars and config files.
- **Read-only mode.** If the server was started with `--read-only`, all `confluence_upload_*` tools refuse with `READ_ONLY_VIOLATION`. `confluence_download_*`, `confluence_compare`, `confluence_ping`, and `confluence_get_page_content` remain available.
- **Space keys (multi-space).** A default `spaceKey` may be configured server-side, and most tools accept an optional `spaceKey` that overrides it. For an **already-synced tree you normally don't need to pass `spaceKey` at all** — each page records its space locally (in the `.id` marker) and the server is the authority. Passing a `spaceKey` that contradicts a page's real space makes the upload tools **error** rather than silently mis-place pages. New child pages are always created in their parent's space (a Confluence tree lives in exactly one space). See "Working with multiple spaces" below.

## Local layout

Each page is a directory containing:
- `index.html` — the page's body in **Confluence storage format** (XHTML with custom tags like `ac:structured-macro`, `ri:user`, `ac:image`, etc.). Treat it as semantic markup, not free-form HTML.
- `.idPAGEID_VER` — a marker file storing the page's Confluence ID and last-synced version (in its name), plus a small JSON body `{"title":…,"space":…}` recording the page's original title and the space it belongs to. Useful for 3-way merges (see below).
- Attachments live as ordinary files in the same directory.
- Child pages live as subdirectories.

## Critical: `outputDir` / `sourceDir` must point to the right place

The server **does not scan the local tree** to locate a page by its Confluence ID.
`pageId` (or `pageTitle`) tells the server *which Confluence page* to work with;
`outputDir` / `sourceDir` tells it *where on the local filesystem* that page's folder
is (or should be created).

| Tool group | Parameter | What it means |
|---|---|---|
| Download tools (`_update`, `_merge`) | `outputDir` | The **parent** directory that will contain the page subfolder (the page is created as a child folder named after its title). Defaults to `"."` (sandbox root) — almost never correct for a targeted page. |
| Upload tools (`_update`, `_merge`, `_create`) | `sourceDir` | The **page folder itself** — the one containing `index.html` and the `.id*` marker. **Required, no default.** |
| `confluence_compare` | `outputDir` | The **parent** directory containing the exported page subfolder. Defaults to `"."`. |

Note the asymmetry: `outputDir` is the **parent** of the page folder, while `sourceDir` **is** the page folder.

**Common mistake:** calling a tool with just `pageId` and no directory path,
expecting the server to find or create the correct subfolder automatically.
The server will *not* search for a `.idPAGEID_*` marker — it operates on
the exact path you provide.

If you don't know the local path for a page, use your filesystem tools
(glob, find, grep) to locate the `.id*` marker file first, then pass its
parent directory as `outputDir`, or the marker's own directory as `sourceDir`.

## Result envelope

Every tool returns one of two shapes:

```jsonc
// Success
{ "success": true,  "summary": "...", "report": { ... }, "logs": [ ... ] }

// Error
{ "success": false, "errorCode": "AUTH_FAILED", "error": "...", "logs": [ ... ] }
```

`errorCode` values you should know:

| Code | Meaning |
|---|---|
| `INVALID_ARGS` | You passed contradictory or missing arguments. Read `error` and try again. |
| `OUT_OF_SANDBOX` | A path argument resolved outside `--root-dir`. Use a path inside the sandbox. |
| `READ_ONLY_VIOLATION` | You called an upload tool on a read-only server. Stop trying. |
| `AUTH_FAILED` | 401/403 from Confluence or a local FS auth error. Report to the user. |
| `PAGE_NOT_FOUND` | Page ID/title doesn't exist on the server. Re-check inputs. |
| `NETWORK_ERROR` | `HttpRequestException` with no status — DNS/TCP/SSL. The server already retried up to 3× with backoff, so this means all retries failed. Call `confluence_ping` to confirm the server can reach Confluence. |
| `INVALID_STATE`, `IO_ERROR`, `INTERNAL` | Other failures. Read `error`. |

The `error` field flattens the full `InnerException` chain joined with `→`, so the root cause is visible without extra calls.

---

## The six sync operations — when to choose which

| Tool | What it does | When to use |
|---|---|---|
| `confluence_download_update` | Force-pull from server, **overwriting local edits** | First export; you don't care about local changes |
| `confluence_download_merge` | Pull only server-side changes, **preserve local edits**; flag conflicts | Normal "fetch latest" workflow |
| `confluence_upload_update` | Force-push local, **overwriting server edits** | You know the server is stale and want your local to win |
| `confluence_upload_create` | Create new pages on the server from a local folder | **Rare.** Only when seeding a brand-new subtree that isn't synced yet (see "Adding a new page" below) |
| `confluence_upload_merge` | Push only local changes, **preserve server edits**; flag conflicts | Normal "publish my edits" workflow — including new pages and moves inside an already-synced subtree |
| `confluence_compare` | Report what differs between server and local; do not change anything | Diagnostic; preview before merge |

The `_merge` variants are the safe defaults. They report conflicts (changes on both sides) in `report.ConflictPages` instead of silently overwriting either side.

## Adding a new page to an already-synced hierarchy

**Do not call `confluence_upload_create` for a single new folder inside a tree that already has `.id*` markers.** It requires you to resolve the parent ID by hand, it fails (silently, no marker written) if any page in the space already has the same title, and on success it only creates one page — siblings are unaffected.

The right tool is `confluence_upload_merge` on a directory at or above the new folder:

1. The agent (or user) creates a new page directory under the existing synced tree — e.g. `<parent-folder>/<NewTitle>/index.html`. Do **not** create a `.id*` marker yourself; the server assigns the page ID.
2. Call `confluence_upload_merge` with `sourceDir` pointing at the parent (or any ancestor) and `recursive: true`.
3. `upload merge` walks the tree, sees the new folder has no marker and no matching server page under the current parent, creates it on Confluence with the correct parent, and writes the `.idPAGEID_VER` marker locally.

`confluence_upload_create` remains correct only when:
- The whole subtree is brand-new on the server (no parent marker available), AND
- You can supply `parentId` or `parentTitle` explicitly.

## Moving or renaming a page locally

If the user moves a page folder to a different location inside the synced tree (the new parent folder must itself have a `.id*` marker), or renames a folder to change the page title, propagate the change with `confluence_upload_merge`:

- Pass `sourceDir` pointing at the moved folder itself, **or** at any ancestor with `recursive: true`. The server-side `ancestors` (parent) and/or title are updated in a single API call; the local `.idPAGEID_VER` marker is refreshed to the new version.
- **Do not run `confluence_download_merge` after a local move and before `upload merge`.** Download treats the server's hierarchy as canonical and will move the local folder back to its original location, undoing the user's intent.
- `confluence_compare` may report the move as "changed on server" when only dates are available — its heuristic compares server `version.when` with local directory mtime. Pass `detectSource: true` to consult version history for a more reliable verdict, but `upload merge` itself does **not** need `compare` to run first.
- If `upload merge` reports the page as skipped with a hint about a deferred move, the server's content was updated since the last sync. Run `confluence_download_merge` for that page, re-apply the local move on the now-up-to-date folder, then run `upload merge` again.

## Working with multiple spaces

A Confluence page tree always lives in a single space, but you can keep trees from **different** spaces side by side under one root and sync them without juggling `spaceKey` per call:

- **Space is remembered per tree.** Every `.id` marker records its page's space (captured from the server). For existing pages the **server is the source of truth**; the marker is just a cache.
- **You usually omit `spaceKey`.** For an already-synced tree, leave `spaceKey` unset — the correct space is resolved from the tree itself. Set `spaceKey` only when seeding brand-new pages at a space root (no parent to inherit from). If you pass a `spaceKey` that disagrees with the page's or parent's real space, the call **errors** so nothing is mis-filed.
- **Cross-space safety.** If a local folder maps to a page that actually lives in a *different* space (a folder moved between two trees on disk, or a hand-edited marker), the upload tools refuse to touch that page or its subtree and record it in the report. Confluence does not move pages across spaces via a parent change — relocate by placing the folder in the correct same-space tree.
- **`multiTree` — sync several trees in one call.** When `sourceDir` is a **container** of multiple tree folders (each child folder has its own `index.html`) rather than a single page folder, pass `multiTree: true` to `confluence_upload_update` / `confluence_upload_merge`. Each tree is processed independently with its own space; one tree's failure is reported without aborting the others. Without `multiTree`, pointing an upload tool at such a container is an error. `multiTree` is incompatible with `pageId`/`pageTitle` (which identify a single page). Download and compare stay single-page-driven — to refresh several trees, call them once per tree.

## Diagnostic and helper tools

| Tool | Purpose |
|---|---|
| `confluence_ping` | Verify the server can reach Confluence with the configured credentials. Returns base URL, current user, latency, sandbox info. **Call this first when something looks broken.** Works in `--read-only` mode. |
| `confluence_get_page_content` | Fetch the storage-format XHTML of a page (current or historical version). Designed for agent-assisted merge — see the workflow below. Works in `--read-only` mode. |

---

## Workflow: resolving a conflict (2-way merge)

When `confluence_download_merge` or `confluence_upload_merge` reports a conflict, the user has edited a page on both sides since the last sync. To help the user merge:

1. **Find the local file.** The page lives at `<root-dir>/<...path...>/index.html`. Read it with your own filesystem tool.
2. **Fetch server-side content.**
   ```jsonc
   confluence_get_page_content {
     "pageId": "<ID from ConflictPages>",
     "normalize": true  // optional; canonicalises attribute order/whitespace
   }
   ```
   The response's `report.content` is the storage-format XHTML the server currently has.
3. **Diff and merge in your head / with your tools.** Confluence storage format is XHTML — diff at the tag-and-text level, not the byte level. Watch for custom tags (`ac:structured-macro`, `ri:user`, `ac:image`); preserve them verbatim across the merge.
4. **Write the merged result** back to `<...>/index.html` using your filesystem edit tool.
5. **Publish.** Call `confluence_upload_update` for that page's parent directory (or `confluence_upload_merge` for one more layer of safety). The version number in the local marker is consumed automatically.

## Workflow: 3-way merge (better)

If you want a real merge base — i.e. the version of the page both sides started from — the local `.idPAGEID_VER` marker stores the version number from the last sync. Add one more call:

```jsonc
confluence_get_page_content {
  "pageId": "<ID>",
  "version": <N from .idPAGEID_VER>
}
```

Now you have three texts:
- **Base** — what the server returned just now with `version=N`.
- **Local** — the current `index.html`.
- **Remote** — what the server returned just now without `version`.

Do a proper 3-way merge: changes present in *local but not base* are user edits; changes in *remote but not base* are server edits; changes in *both relative to base* are the actual conflict — escalate to the user with both options.

## Workflow: troubleshooting connectivity

If a tool fails with `NETWORK_ERROR`, `AUTH_FAILED`, or every call starts failing mid-session (common after the operator's VPN reconnects):

1. Call `confluence_ping`. If it succeeds, the previous error was transient — retry the original call. The server has connection-pool recycling on a 2-minute timer, so things usually self-heal within that window.
2. If `ping` fails with `NETWORK_ERROR`, the server itself cannot reach Confluence. Tell the user; nothing for you to fix.
3. If `ping` fails with `AUTH_FAILED`, the server's stored credentials are bad/expired. Tell the user to update the MCP-server's `CONFLUENCE_EXPORTER__*` env vars.

## Large pages

`confluence_get_page_content` caps the returned content at **256 KB by default** (override with `maxBytes`). If the response has `truncated: true`, **do not try to merge from the partial text** — fetch the page with `confluence_download_update` instead and read the full `index.html` from disk.

---

## Quick cheat sheet

| Situation | Tool to call |
|---|---|
| First-time export of a Confluence subtree | `confluence_download_update` with `recursive: true` |
| Pull latest changes without losing local work | `confluence_download_merge` |
| User edited locally, wants to publish | `confluence_upload_merge` |
| User added a new folder inside an already-synced tree | `confluence_upload_merge` on a parent/ancestor with `recursive: true` (**not** `upload_create`) |
| User moved or renamed a folder inside the synced tree | `confluence_upload_merge` on the moved folder or any ancestor (**not** `download_merge` first — it would undo the move) |
| User wants to know what's different before any sync | `confluence_compare` |
| "Did this break or is it just me?" | `confluence_ping` |
| Got a conflict — need server's version for merge | `confluence_get_page_content` |
| Seeding a brand-new subtree (no parent marker exists) | `confluence_upload_create` with `parentId`/`parentTitle` |
| Publishing several trees from different spaces at once | `confluence_upload_merge` (or `_update`) with `multiTree: true` on the container dir |

## Things the server intentionally does *not* do

- Browse, search, or list pages by metadata.
- Read or edit local files for you — that's your filesystem tools' job.
- Auto-resolve conflicts.
- Comment, like, share, or move pages outside what the upload tools do.

If you find yourself wanting one of these, stop and ask the user — chances are the right answer is a different tool combination, not a feature request.
