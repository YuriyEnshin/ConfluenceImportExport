# Confluence Page Exporter

Утилита командной строки для синхронизации страниц Confluence с локальной структурой папок.

Инструмент поддерживает:

- выгрузку страниц из Confluence на диск (`download`)
- загрузку локальных страниц обратно в Confluence (`upload update`, `upload create`)
- сравнение дерева страниц в Confluence с локальным снимком (`compare`)
- запуск с параметрами из файла конфигурации (`--config`) с приоритетом CLI-аргументов

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

## Файл конфигурации

Утилита поддерживает JSON-файл с параметрами по умолчанию.

- По умолчанию читается `confluence-exporter.json` из текущей директории (если файл отсутствует, утилита работает только с CLI-параметрами).
- Можно явно указать путь: `--config <path-to-json>`.
- Приоритет значений: `CLI` > `config` > встроенное значение по умолчанию.

Пример `confluence-exporter.json`:

```json
{
  "defaults": {
    "baseUrl": "https://wiki.example.com",
    "username": "user@example.com",
    "token": "token-or-password",
    "spaceKey": "DOCS",
    "authType": "onprem",
    "recursive": true,
    "dryRun": false,
    "download": {
      "pageId": "12345",
      "outputDir": "./export",
      "overwriteStrategy": "fail"
    },
    "upload": {
      "update": {
        "sourceDir": "./export/MyPage",
        "onError": "abort",
        "movePages": true
      },
      "create": {
        "sourceDir": "./export/NewPage",
        "parentTitle": "Architecture"
      }
    },
    "compare": {
      "outputDir": "./export",
      "matchByTitle": true
    }
  }
}
```

## Обзор команд

```text
ConfluencePageExporter download ...
ConfluencePageExporter upload update ...
ConfluencePageExporter upload create ...
ConfluencePageExporter compare ...
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
ConfluencePageExporter download \
  --config ./confluence-exporter.json \
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
ConfluencePageExporter upload update \
  --config ./confluence-exporter.json \
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
  --config ./confluence-exporter.json \
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
ConfluencePageExporter compare \
  --config ./confluence-exporter.json \
  --base-url https://wiki.example.com \
  --username user@example.com \
  --token <token> \
  --space-key DOCS \
  --page-id 12345 \
  --recursive \
  --match-by-title \
  --output-dir ./export
```
