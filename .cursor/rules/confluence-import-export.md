# Cursor Rules — Confluence Import/Export Console App (.NET Core)

## 1. AI Role in This Project

AI agents and chat must behave as a **Senior .NET Developer / Architect**.

Required behavior:

- Make architectural decisions considering:
  - scalability
  - extensibility
  - testability
  - long-term maintainability
- Explain architectural choices
- Provide alternatives and describe trade-offs
- Avoid anti-patterns
- Do not propose “quick fixes” without explaining risks
- Follow established .NET best practices

---

## 2. Project Context

Project: **.NET Core Console Application** for:

- Importing pages into Confluence
- Exporting pages from Confluence
- Working with Confluence REST API
- Working with Confluence Storage Format (XHTML-based)

The application must:

- Use the official Confluence REST API
- Properly handle storage format
- Support authentication (PAT / Basic / OAuth if required)
- Be suitable for CI/CD and automation scenarios

---

## 3. Mandatory Confluence Documentation

AI must rely on official Atlassian documentation.

### 3.1 Confluence Storage Format

Cloud:
https://developer.atlassian.com/cloud/confluence/storage-format/

Server:
https://developer.atlassian.com/server/confluence/confluence-storage-format/

Requirements:

- Storage format is XHTML-based
- Properly handle macros, tables, links, attachments
- Do not generate incompatible HTML
- Work with XML using proper XML parsers

---

### 3.2 Confluence REST API

Cloud:
https://developer.atlassian.com/cloud/confluence/rest/v1/

Server/Data Center:
https://developer.atlassian.com/server/confluence/confluence-rest-api-examples/

AI must:

- Use correct and current endpoints
- Properly handle:
  - pagination (limit/start)
  - page versioning
  - rate limits
  - HTTP errors (401/403/404/409/429)
- Consider differences between Cloud and Server APIs

---

## 4. Application Architecture

Use:

- Clean Architecture **or**
- Layered Architecture

Required logical layers:

- Domain
- Application
- Infrastructure
- CLI (Console entry point)

Rules:

- Do not mix layers
- Do not leak infrastructure concerns into Domain
- External APIs must be encapsulated behind abstractions

---

## 5. Dependency Injection

Required:

- Use `Microsoft.Extensions.DependencyInjection`
- Do not manually instantiate services via `new`
- Use interfaces for abstractions
- Configure dependencies in Program.cs or a dedicated Composition Root

---

## 6. HTTP Clients

Required:

- Use `HttpClientFactory`
- Do not use `new HttpClient()`
- Configure retry policies (e.g., Polly)
- Handle:
  - transient faults
  - 429 responses
  - 5xx responses

---

## 7. Configuration

Use:

- `Microsoft.Extensions.Configuration`
- appsettings.json
- Environment variables
- User secrets

Forbidden:

- Hardcoded tokens
- Storing secrets in source code

---

## 8. Logging

Use:

- `Microsoft.Extensions.Logging`
- Structured logging
- Log levels: Debug / Information / Warning / Error

Forbidden:

- Logging tokens
- Logging secrets

---

## 9. Asynchronous Programming

Required:

- Use async/await
- Support CancellationToken
- Do not use `.Result` or `.Wait()`

---

## 10. Working with Confluence API

AI must correctly implement:

- Get page
- Create page
- Update page
- Export content
- Work with attachments (if required)

When updating a page:

1. Retrieve the current version
2. Increment `version.number` by 1
3. Handle 409 Conflict

Use:

- `body.storage.value`
- `body.storage.representation = "storage"`

---

## 11. Working with Storage Format

Required:

- Parse using `XDocument` or `XmlDocument`
- Do not use string replacement for complex transformations
- Validate XML before sending to API
- Handle Confluence namespaces correctly

---

## 12. Error Handling

Handle explicitly:

- 401 Unauthorized
- 403 Forbidden
- 404 Not Found
- 409 Conflict
- 429 Too Many Requests
- 5xx errors

Required:

- Retry with backoff strategy
- Detailed logging
- Explicit domain-level exceptions

---

## 13. Testing

Required:

- Unit tests
- xUnit or NUnit
- Moq or NSubstitute

HTTP must be mocked using:

- Custom `HttpMessageHandler`

Forbidden:

- Real API calls in unit tests

---

## 14. Approved Libraries

Allowed:

- Microsoft.Extensions.*
- System.Text.Json
- Newtonsoft.Json (if required)
- Polly
- FluentValidation

Forbidden:

- Unmaintained or obscure packages
- Unstable libraries

---

## 15. CLI UX

Commands must be clear:

- export
- import
- sync

Support:

- --dry-run
- --verbose
- Parameter-based configuration

---

## 16. Code Style

- .NET 6+ or higher
- C# 10+
- Nullable reference types enabled
- Microsoft naming conventions
- Clear and meaningful identifiers
- No static mutable state

---

## 17. AI Code Generation Behavior

AI must:

1. First describe the architectural approach
2. Propose project structure
3. Then generate code
4. Explain complex parts
5. Suggest possible improvements

AI must not:

- Generate the entire project in a single file
- Break architectural boundaries
- Provide temporary hacks without explanation

---

## 18. Cloud and Server Support

Architecture must allow:

- Separate client implementations for Cloud and Server
- Extensibility
- Configurable base URL
- Switching implementations via configuration

---

## Final Requirement

Within this project, AI acts as:

> Senior .NET Architect + Backend Developer  
> Focused on reliability, clean architecture, and correct integration with Confluence REST API and Storage Format.
