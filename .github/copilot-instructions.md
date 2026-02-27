# Confluence Page Exporter - AI Agent Instructions

## Project Overview
**ConfluencePageExporter** is a .NET console application that recursively exports Confluence page trees to disk as HTML or Markdown files. It authenticates with Confluence (cloud or on-premise) via Basic Auth and traverses the page hierarchy downloading content and attachments.

## Architecture

### Core Components
- **[Program.cs](../Program.cs)**: CLI entry point using `System.CommandLine` (v2.0.2) for parameter parsing
- **[Exporter.cs](../Exporter.cs)**: Main export logic with async HTTP operations and recursive tree traversal
- **[PageData.cs](../PageData.cs)** / **[AttachmentData.cs](../AttachmentData.cs)**: JSON-serialized Confluence REST API response models

### Key Data Flow
1. CLI parses required parameters (base-url, username, token, space-key, parent-id, output-dir)
2. `Exporter` initializes with Basic Auth headers (supports on-prem login:password or cloud email:token)
3. Recursive `DownloadPageTreeAsync()` fetches parent page → attachments → children in sequence
4. `SavePage()` and `SaveAttachments()` write files; paths sanitized with `SanitizeFileName()`
5. Async/await pattern enables concurrent HTTP requests

## Critical Dependencies
- **System.CommandLine 2.0.2**: Handles CLI parsing with options validation
- **Newtonsoft.Json 13.0.4**: Deserializes Confluence REST API JSON responses
- **Microsoft.Extensions.Logging**: Structured logging via ILogger<T> with console output

## Developer Workflows

### Build & Run
```bash
dotnet build
dotnet run -- --base-url https://wiki.example.com --username user@example.com --token xyz123 \
  --space-key DOCS --parent-id 12345 --output-dir ./export
```

### Confluence API Integration
- Endpoints: `/rest/api/content/{pageId}`, `/rest/api/content/{pageId}/child/page`, `/rest/api/content/{pageId}/child/attachment`
- Query params: `expand=body.storage` (for HTML content), `limit=100` (pagination)
- Authentication: Base64-encoded `username:password` or `email:token` in `Authorization: Basic` header
- On-prem mode: Uses `authType="onprem"` parameter (currently hardcoded; consider exposing as CLI flag)

## Code Patterns & Conventions

### Parameter Handling & CLI
- [Program.cs](../Program.cs): Uses System.CommandLine v2.0.2 with `SetAction()` and `ParseResult.GetValue()`
- Options defined with Required=true flag; validation via AcceptOnlyFromAmong() for enums
- **Current limitations**: v2.0.2 lacks short aliases, option descriptions, SetDefaultValue(), and typed SetHandler()
- Help text embedded in RootCommand description with USAGE and EXAMPLE sections (workaround for missing feature parity)
- Default auth-type is "onprem"; explicitly passed to Exporter constructor

### Error Handling
- Attachment fetch failures are logged as warnings but don't halt export (graceful degradation)
- Page tree errors logged with context; uses try-catch wrapping in DownloadPageTreeAsync()
- No retry logic; consider adding for transient HTTP failures

### Output Organization
- Recursively creates subdirectories named after sanitized page titles
- Attachments stored in sibling `attachments/` folder
- File formats: HTML (default) or Markdown based on `--format` option

## Immediate Enhancements (Priority Order)
1. **Upgrade System.CommandLine to v3.0+** - Current v2.0.2 lacks: short aliases (-u, -U, etc.), option descriptions, SetDefaultValue() method, SetHandler() for typed parameters. Upgrade enables professional CLI with better UX.
2. **Implement retry logic** for HTTP transient failures (429, 503) with exponential backoff
3. **Add progress reporting** for large page trees (pages exported / total count)
4. **Validate parentId exists** before recursive traversal starts with dedicated API call
5. **Implement HTML→Markdown conversion** - Currently stub only; consider HtmlAgilityPack + Markdig

## Testing Considerations
- Confluence API mocking: Mock HttpClient responses in unit tests (PageData deserialization, pagination)
- CLI validation: Test required parameters, AcceptOnlyFromAmong() constraints
- Recursive traversal: Test with page hierarchies of varying depth
- Cross-platform paths: Ensure SanitizeFileName() handles non-ASCII Cyrillic characters in bin/export/ tree

## Notes
- Project targets .NET 10.0 and runs on both net10.0 and net8.0 (dual build in bin/)
- Nullable reference types enabled (#nullable enable); ensure all json-deserialized objects handle null defaults
- Logging configured at Information level by default (see ServiceCollection setup in Program.cs)
