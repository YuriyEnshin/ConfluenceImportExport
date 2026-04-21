# Правила для AI-ассистентов

Файлы в этой папке — **не** правила разработки самой утилиты, а **артефакты для пользователей**: их нужно подключить в проект, где лежит выгруженное зеркало Confluence, чтобы AI-ассистент корректно работал с этим деревом (понимал формат `index.html`, назначение маркеров `.id*`, разницу между страницами и вложениями, особенности Confluence Storage Format).

## Доступные правила

- [`local-mirror-format.mdc`](local-mirror-format.mdc) — описание структуры и формата локального зеркала страниц Confluence, выгруженного утилитой.

## Как подключить

Содержимое файла — обычный Markdown с YAML-frontmatter, он инструмент-независим. Выберите способ подключения по вашему AI-ассистенту.

### Cursor

Скопируйте файл в `.cursor/rules/` вашего проекта с выгруженными страницами:

```bash
cp docs/ai-rules/local-mirror-format.mdc /path/to/your-project/.cursor/rules/
```

Правило подхватится автоматически (`alwaysApply: true` во frontmatter).

### Claude Code

Вариант 1 — импортировать в `CLAUDE.md` вашего проекта:

```markdown
@path/to/local-mirror-format.mdc
```

Вариант 2 — скопировать содержимое файла (без frontmatter) прямо в `CLAUDE.md`.

### Другие инструменты (Continue, Aider, Windsurf, ChatGPT и т.п.)

Передайте содержимое файла в system prompt / rules / context по правилам вашего инструмента. Frontmatter-блок (между `---`) можно удалить — он используется только Cursor.
