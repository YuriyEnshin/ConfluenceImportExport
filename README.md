# Confluence Page Exporter

Command-line utility for synchronizing Confluence pages with a local folder tree.

The tool supports:

- downloading pages from Confluence to disk (`download`)
- uploading local pages back to Confluence (`upload update`, `upload create`)
- comparing Confluence tree with local snapshot (`compare`)

## Key Features

- Page selection by `--page-id` or `--page-title`
- Optional recursive processing (`--recursive`)
- Local snapshot format:
  - one folder per page (folder name = page title, sanitized for filesystem)
  - `index.html` with page storage content
  - attachments as separate files
  - `.id<pageId>` marker file for stable page identity
- Authentication modes:
  - `--auth-type onprem`
  - `--auth-type cloud`
- Dry-run support for operations where relevant

## Build

```bash
dotnet build
```

## Command Overview

```text
ConfluencePageExporter download ...
ConfluencePageExporter upload update ...
ConfluencePageExporter upload create ...
ConfluencePageExporter compare ...
```

## Download

Exports a Confluence page (or page subtree) to local disk.

### Download Parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--page-id` or `--page-title` (exactly one required)
- `--output-dir` (required)
- `--recursive` (optional)
- `--overwrite-strategy skip|overwrite|fail` (optional, default `fail`)
- `--auth-type onprem|cloud` (optional, default `onprem`)
- `--dry-run` (optional)

### Behavior Notes

- If a page already exists locally by `.id<pageId>`, the tool can move/rename its folder to match the current Confluence hierarchy.
- If local `index.html` content is unchanged, the file is not rewritten.
- In dry-run mode, the full algorithm runs, but files and folders are not modified.

### Download Example

```bash
ConfluencePageExporter download \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Upload Update

Updates existing Confluence page(s) from local folder content.

### Upload Update Parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--source-dir` (required)
- `--page-id` or `--page-title` (optional, explicit root target)
- `--recursive` (optional)
- `--on-error abort|skip` (optional, default `abort`)
- `--auth-type onprem|cloud` (optional, default `onprem`)
- `--dry-run` (optional)

### Root Resolution Priority

1. explicit `--page-id` / `--page-title`
2. local `.id<pageId>` marker in source folder
3. source folder name as page title

If no page is found, update command fails for the root page.

### Upload Update Example

```bash
ConfluencePageExporter upload update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --source-dir ./export/MyPage \
  --recursive
```

## Upload Create

Creates new Confluence page(s) from local folder content.

### Upload Create Parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--source-dir` (required)
- `--parent-id` or `--parent-title` (optional)
- `--recursive` (optional)
- `--auth-type onprem|cloud` (optional, default `onprem`)
- `--dry-run` (optional)

### Upload Create Example

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

## Compare

Compares Confluence page tree with local snapshot and prints a report.

### Compare Parameters

- `--base-url` (required)
- `--username` (required)
- `--token` (required)
- `--space-key` (required)
- `--page-id` or `--page-title` (exactly one required)
- `--output-dir` (required)
- `--recursive` (optional)
- `--match-by-title` (optional)
- `--auth-type onprem|cloud` (optional, default `onprem`)

### Matching Strategy

- default: match local pages by `.id<pageId>`
- with `--match-by-title`: if local `.id` is missing, try fallback matching by title/folder path

### Report Sections

- pages added in Confluence
- pages deleted in Confluence
- pages renamed and/or moved in Confluence
- pages with changed content

### Compare Example

```bash
ConfluencePageExporter compare \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --match-by-title \
  --output-dir ./export
```
