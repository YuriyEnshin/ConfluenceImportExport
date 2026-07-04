# Confluence Page Exporter — инструкции для ИИ-агентов

CLI-утилита **и** MCP-сервер на **.NET 10** для двусторонней синхронизации
дерева страниц Confluence (Server/DC и Cloud) с локальным деревом папок.
Зрелый проект в стадии сопровождения (v2.18.0+) — **расширяй существующее, не
генерируй с нуля**.

## Карта проекта

- `src/ConfluencePageExporter/`
  - `Commands/` — CLI-команды (`download`/`upload`/`compare`/`config`), `System.CommandLine`
  - `Services/` — логика синхронизации (download/upload/merge/compare, анализ конфликтов)
  - `Infrastructure/` — HTTP (`RetryingHttpHandler`), DI (`ServiceCollectionExtensions`), нормализация, `CommandDispatcher`, `McpServerRunner`
  - `Models/` — DTO Confluence и доменные модели
  - `Options/` — конфигурация (приоритет CLI > env > файл > default)
  - `Tools/` — обёртки MCP-инструментов
  - `Program.cs` → `CommandDispatcher` (CLI) либо `McpServerRunner` (MCP по stdio)
- `tests/ConfluencePageExporter.Tests/` — xunit.v3 (MTP) + Moq + Shouldly
- `docs/` — двуязычная документация + поставляемые пользователям артефакты

**Двойная поверхность:** одна и та же логика синка доступна как CLI и как
MCP-сервер. `docs/mcp/agent-instructions.md` встроен в сборку как
`EmbeddedResource`.

## Критические инварианты (не нарушать)

- **Контракт нормализации.** Любое изменение нормализации контента
  (`XmlContentNormalizer` / `RegexContentNormalizer` / `HtmlEntities` / правила
  атрибутов и пробелов / алгоритм хеша / смена активного `IContentNormalizer`)
  ОБЯЗАНО поднять `NormalizationContract.CurrentEpoch` и обновить golden-значение,
  иначе `ContentHasherTests` падает и хеши молча расходятся. Детали — в
  [dotnet-maintenance](.claude/rules/dotnet-maintenance.md).
- **Двуязычная документация.** Правка любого `*.md` из пары синхронно правится
  в `*.en.md` в том же коммите.
- **CHANGELOG.** Видимые пользователю изменения фиксируются в `CHANGELOG.md` +
  `CHANGELOG.en.md` в том же коммите.
- **Сборка.** `TreatWarningsAsErrors=true` — любой варнинг валит билд.
  `Nullable` и `ImplicitUsings` включены.
- **Язык.** Документация и сообщения коммитов/PR — на русском (канон). Правила
  и комментарии в коде допустимы на английском.

## Как гонять тесты

xunit.v3 на Microsoft Testing Platform: `dotnet test` **не печатает результаты**.
Собери тест-проект и запусти `.exe` напрямую:

```powershell
dotnet build tests/ConfluencePageExporter.Tests/ConfluencePageExporter.Tests.csproj
tests/ConfluencePageExporter.Tests/bin/Debug/net10.0/ConfluencePageExporter.Tests.exe
# фильтр по имени метода:
# ... ConfluencePageExporter.Tests.exe --filter-method "*ShouldEscapeQuotesInCql*"
```

## Детальные правила

@.claude/rules/dotnet-maintenance.md
@.claude/rules/changelog.md
@.claude/rules/documentation-translations.md
