# AGENTS.md

Инструкции для ИИ-агентов в этом репозитории. **Единый источник** — `CLAUDE.md`
(карта проекта, критические инварианты, запуск тестов) и правила в
`.claude/rules/`:

- `.claude/rules/dotnet-maintenance.md` — сопровождение .NET-кода, Confluence
  Storage Format / REST API, MCP-сервер, стек и конвенции, релиз
- `.claude/rules/changelog.md` — ведение CHANGELOG
- `.claude/rules/documentation-translations.md` — двуязычная документация

Claude Code читает `CLAUDE.md`. Этот файл существует для агентов, читающих
стандарт `AGENTS.md` (Codex и др.); инструменты с поддержкой `@import`
подхватят правила ниже, остальным — открыть файлы по путям выше.

@.claude/rules/dotnet-maintenance.md
@.claude/rules/changelog.md
@.claude/rules/documentation-translations.md
