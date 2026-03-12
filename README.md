# Confluence Page Exporter

Утилита командной строки для синхронизации страниц Confluence с локальной структурой папок.

Инструмент поддерживает:

- выгрузку страниц из Confluence на диск (`download`)
- загрузку локальных страниц обратно в Confluence (`upload update`, `upload create`)
- сравнение дерева страниц в Confluence с локальным снимком (`compare`)
- просмотр действующей конфигурации с указанием источников значений (`config show`)

## Основные возможности

- Выбор страницы по `--page-id` или `--page-title`
- Опциональная рекурсивная обработка (`--recursive`)
- Формат локального снимка:
  - одна папка на страницу (имя папки = заголовок страницы, с sanitization под файловую систему)
  - файл `index.html` с контентом страницы в storage representation
  - вложения как отдельные файлы
  - файл-маркер `.id<pageId>` для стабильной идентификации страницы
- Режимы аутентификации:
  - `--auth-type onprem`
  - `--auth-type cloud`
- Многоуровневая конфигурация с приоритетом: CLI > переменные окружения > файл > значение по умолчанию
- Глобальный параметр `--verbose` для подробного (debug-level) вывода
- Поддержка dry-run там, где применимо

## Локальная структура хранения

При выгрузке (`download`) страницы сохраняются в иерархию папок внутри `--output-dir`.
Каждая папка страницы содержит контент, маркер идентификатора и вложения.

```text
<output-dir>/
  Root Page/
    index.html
    .id12345
    image.png
    spec.pdf
    Child Page A/
      index.html
      .id23456
    Child Page B/
      index.html
      .id34567
```

Правила:

- имя папки страницы = заголовок страницы в Confluence (с заменой недопустимых символов)
- `index.html` содержит `body.storage.value`
- файл `.id<pageId>` используется для стабильного сопоставления при `download`, `upload update` и `compare`
- все файлы, кроме `index.html` и `.id*`, считаются вложениями страницы

## Сборка

```bash
dotnet build
```

## Конфигурация

Утилита использует стандартный `Microsoft.Extensions.Configuration` pipeline. Параметры читаются из нескольких источников с приоритетом (последний побеждает):

1. **JSON-файл** — по умолчанию `confluence-exporter.json` в текущей директории; путь можно указать явно через `--config <path>`.
2. **Переменные окружения** — с префиксом `CONFLUENCE_EXPORTER__`, двойное подчёркивание разделяет секции (например, `CONFLUENCE_EXPORTER__GLOBAL__BASEURL`).
3. **Параметры командной строки** — явно заданные CLI-аргументы имеют наивысший приоритет.

### Глобальные параметры

Указываются перед именем команды:

- `--config <path>` — путь к JSON-файлу конфигурации
- `--verbose` — включить подробный (debug-level) вывод в лог

### Пример `confluence-exporter.json`

```json
{
  "Global": {
    "BaseUrl": "https://wiki.example.com",
    "Username": "user@example.com",
    "Token": "token-or-password",
    "SpaceKey": "DOCS",
    "AuthType": "onprem",
    "DryRun": false,
    "Recursive": true
  },
  "Download": {
    "PageId": "12345",
    "OutputDir": "./export",
    "OverwriteStrategy": "fail"
  },
  "Upload": {
    "Update": {
      "SourceDir": "./export/MyPage",
      "OnError": "abort",
      "MovePages": true
    },
    "Create": {
      "SourceDir": "./export/NewPage",
      "ParentTitle": "Architecture"
    }
  },
  "Compare": {
    "OutputDir": "./export",
    "MatchByTitle": true
  }
}
```

### Переменные окружения

Переменные окружения именуются по формату `CONFLUENCE_EXPORTER__<Секция>__<Параметр>` (всё в верхнем регистре):

```bash
export CONFLUENCE_EXPORTER__GLOBAL__BASEURL=https://wiki.example.com
export CONFLUENCE_EXPORTER__GLOBAL__USERNAME=user@example.com
export CONFLUENCE_EXPORTER__GLOBAL__TOKEN=secret
export CONFLUENCE_EXPORTER__DOWNLOAD__OUTPUTDIR=./export
```

## Обзор команд

```text
ConfluencePageExporter download ...
ConfluencePageExporter upload update ...
ConfluencePageExporter upload create ...
ConfluencePageExporter compare ...
ConfluencePageExporter config show
```

## Команда download

Выгружает страницу Confluence (или поддерево страниц) на локальный диск.

### Параметры download

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--page-id` или `--page-title` (обязательно указать ровно один)
- `--output-dir` (обязательный)
- `--recursive` (опционально)
- `--overwrite-strategy skip|overwrite|fail` (опционально, по умолчанию `fail`)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)

### Особенности поведения

- Если локальная страница уже существует по `.id<pageId>`, утилита может переименовать/переместить папку в соответствии с актуальной иерархией Confluence.
- Если содержимое локального `index.html` не изменилось, файл не перезаписывается.
- В dry-run выполняется полный алгоритм, но без фактических изменений файлов и папок.

### Пример download

```bash
ConfluencePageExporter --config ./confluence-exporter.json download \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Команда upload update

Обновляет существующие страницы Confluence по локальному содержимому.

### Параметры upload update

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--source-dir` (обязательный)
- `--page-id` или `--page-title` (опционально, явное указание корневой страницы)
- `--recursive` (опционально)
- `--on-error abort|skip` (опционально, по умолчанию `abort`)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)

### Приоритет определения корневой страницы

1. Явно заданные `--page-id` / `--page-title`
2. Локальный файл-маркер `.id<pageId>` в `source-dir`
3. Имя папки `source-dir` как заголовок страницы

Если корневая страница не найдена, команда завершается ошибкой.

### Пример upload update

```bash
ConfluencePageExporter --config ./confluence-exporter.json upload update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --source-dir ./export/MyPage \
  --recursive
```

## Команда upload create

Создает новые страницы Confluence по локальному содержимому.

### Параметры upload create

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--source-dir` (обязательный)
- `--parent-id` или `--parent-title` (опционально)
- `--recursive` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)

### Пример upload create

```bash
ConfluencePageExporter --config ./confluence-exporter.json upload create \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --parent-id 67890 \
  --source-dir ./export/NewPage \
  --recursive
```

## Команда compare

Сравнивает дерево страниц в Confluence с локальным снимком и выводит отчет.

### Параметры compare

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--page-id` или `--page-title` (обязательно указать ровно один)
- `--output-dir` (обязательный)
- `--recursive` (опционально)
- `--match-by-title` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)

### Стратегия сопоставления

- по умолчанию: сопоставление локальных страниц по `.id<pageId>`
- с `--match-by-title`: если `.id` отсутствует, используется fallback-сопоставление по заголовкам/пути папок

### Разделы отчета

- страницы, добавленные в Confluence
- страницы, удаленные из Confluence
- страницы, переименованные и/или перемещенные в Confluence
- страницы с измененным контентом

### Пример compare

```bash
ConfluencePageExporter --config ./confluence-exporter.json compare \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --match-by-title \
  --output-dir ./export
```

## Команда config show

Выводит текущую действующую конфигурацию с указанием источника каждого значения.

Возможные источники:

- `[CLI]` — задано аргументом командной строки
- `[ENV]` — задано переменной окружения
- `[FILE]` — задано в JSON-файле конфигурации
- `[DEFAULT]` — значение по умолчанию

### Пример config show

```bash
ConfluencePageExporter --config ./confluence-exporter.json config show
```

Пример вывода:

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
```

## Подробное логирование

Для включения debug-level вывода используйте глобальный параметр `--verbose`:

```bash
ConfluencePageExporter --verbose download \
  --page-id 12345 \
  --output-dir ./export
```
