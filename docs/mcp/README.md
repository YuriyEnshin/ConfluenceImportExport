# MCP-сервер Confluence Page Exporter

Confluence Page Exporter может работать как сервер
[Model Context Protocol (MCP)](https://modelcontextprotocol.io) — это
позволяет ИИ-агентам (Claude Desktop, Cursor, Codex CLI, Continue и др.)
выполнять синхронизацию страниц Confluence с локальным деревом тем же
способом, что и человек из CLI.

## Запуск

```bash
ConfluencePageExporter mcp --root-dir <path> [--read-only]
```

Параметры:

| Параметр | Назначение |
|---|---|
| `--root-dir <path>` | **Обязательный.** Корневая папка-песочница. Все пути, переданные агентом в инструменты, резолвятся относительно неё. Абсолютные пути допускаются, только если лежат внутри `root-dir`. |
| `--read-only` | Опционально. Блокирует все upload-инструменты (`download`/`compare` остаются доступными). Полезно, когда агенту нужен доступ только на чтение. |

Параметры подключения к Confluence (`BaseUrl`, `Username`, `Token`,
`SpaceKey`, `AuthType`) задаются через те же механизмы, что и для CLI:

1. Переменные окружения с префиксом `CONFLUENCE_EXPORTER__` —
   **рекомендуемый способ** для MCP, прописываются в конфиге MCP-клиента.
2. JSON-конфиг через `--config <path>` (тот же формат, что для CLI).

Аутентификационные параметры **никогда не передаются в инструменты от
агента** — это сделано намеренно, чтобы токен не попадал в context
window LLM.

## Инструменты

| Имя | Назначение | CLI-аналог |
|---|---|---|
| `confluence_download_update` | Скачать страницы с принудительной перезаписью локальных файлов | `download update` |
| `confluence_download_merge` | Скачать только серверные изменения, сохранив локальные правки | `download merge` |
| `confluence_upload_update` | Залить локальные страницы, перезаписав серверные изменения | `upload update` |
| `confluence_upload_create` | Создать новые страницы в Confluence из локальной папки | `upload create` |
| `confluence_upload_merge` | Залить только локальные изменения, сохранив серверные правки | `upload merge` |
| `confluence_compare` | Сравнить дерево Confluence с локальной копией | `compare` |
| `confluence_ping` | **Диагностика.** Проверить связность и учётные данные одним лёгким запросом; вернуть base URL, текущего пользователя, latency и настройки песочницы. Работает и в `--read-only`. | — |

Все инструменты возвращают JSON-конверт единого формата:

**Успех:**
```json
{
  "success": true,
  "summary": "Download merge completed in C:\\confluence-mirror\\DOCS; 0 conflict(s).",
  "report": { /* SyncReport или CompareReport, если report=true */ },
  "logs": [ "Download merge: page ID '123'...", "..." ]
}
```

**Ошибка:**
```json
{
  "success": false,
  "errorCode": "OUT_OF_SANDBOX",
  "error": "Path '../etc' resolves to '...' which is outside the sandbox root '...'.",
  "logs": [ "..." ]
}
```

### Коды ошибок

| `errorCode` | Когда возникает |
|---|---|
| `INVALID_ARGS` | Неверная комбинация параметров (например, заданы и `pageId`, и `pageTitle`); отсутствует `spaceKey` и в конфиге, и в аргументах. |
| `OUT_OF_SANDBOX` | Переданный путь после нормализации оказался вне `--root-dir`. |
| `READ_ONLY_VIOLATION` | Попытка вызвать upload-инструмент на сервере с флагом `--read-only`. |
| `AUTH_FAILED` | 401/403 от Confluence или `UnauthorizedAccessException` на файловой системе. |
| `NETWORK_ERROR` | `HttpRequestException` без HTTP-статуса (DNS, TCP, SSL EOF и т.п.). Если повторяется после `confluence_ping` — проверь сеть/VPN. |
| `PAGE_NOT_FOUND` | 404 от Confluence или невозможно разрешить страницу по `pageId`/`pageTitle`. |
| `DIRECTORY_NOT_FOUND`, `FILE_NOT_FOUND` | Локальный путь не существует. |
| `INVALID_STATE`, `IO_ERROR`, `INTERNAL` | Прочие ошибки исполнения. |

## Песочница

Песочница (`--root-dir`) — это **жёсткий инвариант безопасности**:
агент, подключённый к серверу, не может:

- передать инструменту путь за пределами `--root-dir`,
- переопределить `--root-dir` через файл конфигурации или переменную
  окружения,
- разблокировать upload-инструменты, если сервер запущен с `--read-only`.

Параметры `--root-dir` и `--read-only` принимаются **только** из
аргументов командной строки команды `mcp` (не из IConfiguration), что
гарантирует невозможность их изменения через окружение или конфиг-файл.

## Примеры конфигов клиентов

- [Claude Desktop](claude-desktop.json) — `claude_desktop_config.json`
- [Cursor](cursor.json) — `.cursor/mcp.json` в проекте
- [Codex CLI](codex.toml) — `~/.codex/config.toml`

Во всех примерах токен Confluence нужно подставить вместо
`<API_TOKEN>`.

## Что делает агент после получения результата

Поскольку MCP-инструменты выполняют физическую запись на диск (в
`--root-dir`) или в Confluence, агент **может и должен** работать с
результирующим деревом своими собственными инструментами файловой
системы (Read, Grep, Bash и др.). MCP-сервер сознательно не дублирует
эту функциональность — он отвечает только за синхронизацию.
