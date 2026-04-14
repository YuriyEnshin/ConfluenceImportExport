# Confluence Page Exporter v2.3

Утилита командной строки для синхронизации страниц Confluence с локальной структурой папок.

Инструмент поддерживает:

- скачивание страниц из Confluence на диск с принудительной перезаписью (`download update`) или умным слиянием (`download merge`)
- загрузку локальных страниц обратно в Confluence с принудительной перезаписью (`upload update`), умным слиянием (`upload merge`) или созданием новых (`upload create`)
- сравнение дерева страниц в Confluence с локальным снимком (`compare`)
- просмотр действующей конфигурации с указанием источников значений (`config show`)

## Основные возможности

- Git-подобная модель синхронизации: `update` (force) и `merge` (smart)
- Определение конфликтов: если страница изменена и локально, и на сервере — конфликт выявляется, перезапись не выполняется, пользователь получает предупреждение
- Отчёт `--report` — сводка страниц, требующих ручного разрешения (конфликты, удалённые страницы)
- Выбор страницы по `--page-id` или `--page-title`
- Опциональная рекурсивная обработка (`--recursive`)
- Формат локального снимка:
  - одна папка на страницу (имя папки = заголовок страницы, с sanitization под файловую систему)
  - файл `index.html` с контентом страницы в storage representation
  - вложения как отдельные файлы
  - файл-маркер `.id<pageId>_<version>` для стабильной идентификации страницы и отслеживания версии
- Режимы аутентификации: `--auth-type onprem` и `--auth-type cloud`
- Многоуровневая конфигурация с приоритетом: CLI > переменные окружения > файл > значение по умолчанию
- Глобальный параметр `--verbose` для подробного (debug-level) вывода
- Поддержка dry-run там, где применимо

## Локальная структура хранения

При выгрузке (`download update`/`download merge`) страницы сохраняются в иерархию папок внутри `--output-dir`.
Каждая папка страницы содержит контент, маркер идентификатора и вложения.

```text
<output-dir>/
  Root Page/
    index.html
    .id12345_7
    image.png
    spec.pdf
    Child Page A/
      index.html
      .id23456_3
    Child Page B/
      index.html
      .id34567_1
```

Правила:

- имя папки страницы = заголовок страницы в Confluence (каждый недопустимый символ заменяется на `_`); оригинальный заголовок сохраняется в маркере `.id*` и восстанавливается при загрузке на сервер; переименование папки пользователем интерпретируется как намерение переименовать страницу
- `index.html` содержит `body.storage.value`
- файл `.id<pageId>_<version>` используется для стабильного сопоставления при синхронизации и сравнении; суффикс `_<version>` отражает номер версии страницы на сервере в момент последней синхронизации; время последней записи маркера (`LastWriteTimeUtc`) используется как точка отсчёта для определения конфликтов; содержимое файла хранит оригинальный заголовок страницы Confluence для восстановления при загрузке на сервер
- все файлы, кроме `index.html` и `.id*`, считаются вложениями страницы

## Правило для AI-ассистентов (Cursor / Codex)

В корне репозитория находится файл `cursor_rule.mdc` — правило для AI-ассистентов, описывающее структуру и формат локального зеркала страниц Confluence.

Если вы используете AI-ассистент (Cursor, Codex и т.д.) для работы с выгруженными страницами, добавьте это правило в конфигурацию вашего проекта — AI будет корректно понимать иерархию папок, формат `index.html` (Confluence Storage Format), маркеры `.id*` и вложения.

### Подключение в Cursor

Скопируйте файл в папку `.cursor/rules/` вашего проекта с выгруженными страницами:

```bash
cp cursor_rule.mdc /path/to/your-project/.cursor/rules/confluence-storage.mdc
```

После этого правило будет автоматически применяться при работе с файлами проекта.

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
- `--report` — вывести сводку страниц, требующих ручной обработки, после завершения команды

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
    "Recursive": true,
    "Report": false
  },
  "Download": {
    "PageId": "12345",
    "OutputDir": "./export",
    "Merge": {
      "OutputDir": "./export-merge"
    }
  },
  "Upload": {
    "SourceDir": "./export",
    "Update": {
      "PageId": "67890"
    },
    "Create": {
      "ParentTitle": "Architecture"
    },
    "Merge": {
      "PageTitle": "MyPage"
    }
  },
  "Compare": {
    "OutputDir": "./export",
    "MatchByTitle": true,
    "DetectSource": false
  }
}
```

### Наследование параметров

Конфигурация поддерживает двухуровневую модель: общие параметры команды наследуются подкомандами и могут быть переопределены на уровне подкоманды.

Цепочка разрешения значений (от высшего приоритета к низшему):

1. **Секция подкоманды** — например, `Download:Update:OutputDir`
2. **Секция команды** — например, `Download:OutputDir`
3. **Global** — для параметра `Recursive` (fallback в коде хэндлера)
4. **Значение по умолчанию** — `false` / `null`

Пример: если в JSON указан `"Download": { "PageId": "12345", "OutputDir": "./export", "Merge": { "OutputDir": "./export-merge" } }`, то:
- `download update` получит `PageId = 12345`, `OutputDir = ./export` (наследование от `Download`)
- `download merge` получит `PageId = 12345` (наследование), `OutputDir = ./export-merge` (переопределение)

### Переменные окружения

Переменные окружения именуются по формату `CONFLUENCE_EXPORTER__<Секция>__<Параметр>` (всё в верхнем регистре):

```bash
export CONFLUENCE_EXPORTER__GLOBAL__BASEURL=https://wiki.example.com
export CONFLUENCE_EXPORTER__GLOBAL__USERNAME=user@example.com
export CONFLUENCE_EXPORTER__GLOBAL__TOKEN=secret
export CONFLUENCE_EXPORTER__DOWNLOAD__OUTPUTDIR=./export
export CONFLUENCE_EXPORTER__DOWNLOAD__UPDATE__PAGEID=12345
export CONFLUENCE_EXPORTER__UPLOAD__SOURCEDIR=./export
```

## Формат вызова

```text
ConfluencePageExporter [глобальные параметры] <команда подкоманда> [параметры команды]
```

## Обзор команд

```text
ConfluencePageExporter download update ...    # принудительное скачивание (сервер → локально)
ConfluencePageExporter download merge ...     # умное скачивание с сохранением локальных правок
ConfluencePageExporter upload update ...      # принудительная загрузка (локально → сервер)
ConfluencePageExporter upload merge ...       # умная загрузка с сохранением серверных правок
ConfluencePageExporter upload create ...      # создание новых страниц
ConfluencePageExporter compare ...            # сравнение и отчёт
ConfluencePageExporter config show            # отображение конфигурации
```

## Модель синхронизации

Утилита использует git-подобную модель с двумя режимами:

| Режим | Описание |
|-------|----------|
| **update** | Принудительная синхронизация. Источник считается эталоном, целевая сторона перезаписывается. Локальные/серверные правки на целевой стороне будут потеряны. |
| **merge** | Умная синхронизация. Перезаписываются только страницы, изменённые на стороне-источнике. Правки на целевой стороне сохраняются. Страницы с конфликтом (двойные правки) пропускаются с предупреждением. |

### Типичные сценарии использования

```bash
# Полностью скачать серверную версию, затерев локальные изменения
ConfluencePageExporter download update --page-id 12345 --output-dir ./export --recursive

# Скачать только серверные обновления, сохранив локальные правки
ConfluencePageExporter download merge --page-id 12345 --output-dir ./export --recursive

# Загрузить локальные изменения на сервер, затерев серверные
ConfluencePageExporter upload update --source-dir ./export/MyPage --recursive

# Загрузить только локальные обновления, сохранив серверные правки
ConfluencePageExporter upload merge --source-dir ./export/MyPage --recursive

# Двусторонняя синхронизация (сохраняются самые новые изменения с обеих сторон)
ConfluencePageExporter download merge --page-id 12345 --output-dir ./export --recursive --report
ConfluencePageExporter upload merge --source-dir ./export/MyPage --recursive --report
```

### Определение конфликтов

В режиме `merge` утилита использует маркер `.id<pageId>_<version>` для определения конфликтов:

- **syncTimeUtc** = время последней записи маркерного файла (момент последней синхронизации)
- **serverChanged** = серверная версия новее версии в маркере
- **localChanged** = файл `index.html` изменён после `syncTimeUtc`
- Если оба флага `true` → **конфликт**: страница не перезаписывается ни в одну сторону, выводится предупреждение

При наличии ключа `--report` после завершения команды выводится сводка всех страниц, требующих ручного разрешения.

## Команда download update

Принудительно скачивает страницу Confluence (или поддерево) на локальный диск. Различающиеся страницы перезаписываются серверными версиями. Локальные правки будут потеряны.

### Параметры download update

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--page-id` или `--page-title` (обязательно указать ровно один)
- `--output-dir` (обязательный)
- `--recursive` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)
- `--report` (опционально)

### Пример download update

```bash
ConfluencePageExporter download update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Команда download merge

Скачивает страницы с сервера, перезаписывая только те, которые новее на сервере. Локальные правки сохраняются. Конфликты (двойные правки) пропускаются с предупреждением.

### Параметры download merge

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--page-id` или `--page-title` (обязательно указать ровно один)
- `--output-dir` (обязательный)
- `--recursive` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)
- `--report` (опционально)

### Пример download merge

```bash
ConfluencePageExporter --report download merge \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --output-dir ./export
```

## Команда upload update

Принудительно загружает локальные страницы на сервер. Различающиеся страницы перезаписываются локальными версиями. Серверные правки будут потеряны. Перемещение страниц при расхождении родителя выполняется автоматически.

### Параметры upload update

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--source-dir` (обязательный)
- `--page-id` или `--page-title` (опционально, явное указание корневой страницы)
- `--recursive` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)
- `--report` (опционально)

### Приоритет определения корневой страницы

1. Явно заданные `--page-id` / `--page-title`
2. Локальный файл-маркер `.id<pageId>_<version>` в `source-dir`
3. Имя папки `source-dir` как заголовок страницы

Если корневая страница не найдена, команда завершается ошибкой.

### Пропуск неизменённых страниц

Перед отправкой обновления утилита сравнивает локальный контент с серверным. Если заголовок, контент и родительская страница не изменились, обновление пропускается — это предотвращает создание лишних версий на сервере.

### Обновление вложений

Вложения обновляются через версионирование Confluence (создание новой версии файла). Перед загрузкой проверяется, изменился ли файл (по размеру и SHA-256 хэшу). Неизменённые вложения пропускаются.

### Пример upload update

```bash
ConfluencePageExporter upload update \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --source-dir ./export/MyPage \
  --recursive
```

## Команда upload merge

Загружает на сервер только локально изменённые страницы. Серверные правки сохраняются. Конфликты (двойные правки) пропускаются с предупреждением.

### Параметры upload merge

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--source-dir` (обязательный)
- `--page-id` или `--page-title` (опционально, явное указание корневой страницы)
- `--recursive` (опционально)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)
- `--dry-run` (опционально)
- `--report` (опционально)

### Пример upload merge

```bash
ConfluencePageExporter --report upload merge \
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
ConfluencePageExporter upload create \
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
Для каждого обнаруженного различия определяется вероятный источник изменения (сервер или локально) на основе сравнения дат модификации.
При использовании `--detect-source` дополнительно анализируется история версий Confluence для повышения точности определения источника переименований и перемещений.

### Параметры compare

- `--base-url` (обязательный)
- `--username` (обязательный)
- `--token` (обязательный)
- `--space-key` (обязательный)
- `--page-id` или `--page-title` (обязательно указать ровно один)
- `--output-dir` (обязательный)
- `--recursive` (опционально)
- `--match-by-title` (опционально)
- `--detect-source` (опционально) — анализировать историю версий для определения источника переименований и перемещений (дополнительные API-вызовы)
- `--auth-type onprem|cloud` (опционально, по умолчанию `onprem`)

### Стратегия сопоставления

- по умолчанию: сопоставление локальных страниц по `.id<pageId>_<version>`
- с `--match-by-title`: если `.id` отсутствует, используется fallback-сопоставление по заголовкам/пути папок

### Определение источника изменений

Для каждого обнаруженного различия утилита пытается определить, где произошло изменение — локально или на сервере. Используется два уровня эвристик:

1. **Сравнение версий маркера** (для контента, если доступен `.id<pageId>_<version>`) — если версия маркера совпадает с серверной, изменение локальное; если серверная версия новее — изменение на сервере. Достоверность: высокая.

2. **Сравнение дат** (всегда, как fallback) — сопоставляет дату последней модификации серверной страницы (`version.when`) с датой изменения локальной папки (переименование/перемещение) или файла `index.html` (контент). Достоверность: средняя.

3. **Анализ истории версий** (с `--detect-source`) — для переименований ищет прежний заголовок в истории версий страницы, для перемещений ищет прежнего родителя в ancestors исторических версий. Достоверность: высокая.

### Пример compare

```bash
ConfluencePageExporter compare \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --match-by-title \
  --detect-source \
  --output-dir ./export
```

Пример вывода:

```text
Compare report
==============
Added in Confluence: 1
  + [55555] New Page (Root/New Page)
Deleted in Confluence: 0
Renamed/moved: 1
  ~ [12345] New Title | local: Root/Old Title -> confluence: Root/New Title
    Переименование: СЕРВЕР (высокая) — заголовок 'Old Title' найден в серверной версии 3
Content changed: 1
  * [23456] Some Page (Root/Some Page)
    Источник: ЛОКАЛЬНО (средняя) — локальный файл изменён (2026-03-12) позже сервера (2026-03-10)
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
ConfluencePageExporter config show
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
  Report                       = False                               [DEFAULT]
```

## Подробное логирование

Для включения debug-level вывода используйте глобальный параметр `--verbose`:

```bash
ConfluencePageExporter --verbose download update \
  --page-id 12345 \
  --output-dir ./export
```

## Миграция с v1.x

В версии 2.0 произведены ломающие изменения в структуре команд:

| v1.x | v2.0 | Описание |
|------|------|----------|
| `download` | `download update` | Принудительное скачивание |
| — | `download merge` | Умное скачивание (новая команда) |
| `upload update --on-error abort` | `upload update` | Параметр `--on-error` удалён; при ошибке выполнение прерывается |
| `upload update --move-pages` | `upload update` | Параметр `--move-pages` удалён; перемещение выполняется автоматически |
| `download --overwrite-strategy overwrite` | `download update` | Параметр `--overwrite-strategy` удалён |
| — | `upload merge` | Умная загрузка (новая команда) |
| — | `--report` | Отчёт о страницах с конфликтами (новый глобальный ключ) |
