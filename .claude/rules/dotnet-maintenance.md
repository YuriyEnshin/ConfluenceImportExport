# Сопровождение утилиты Confluence Page Exporter (.NET)

## Роль

Senior .NET разработчик / архитектор в режиме **сопровождения зрелого проекта**
(v2.18.0+). Проект уже построен — не генерируй его с нуля и не переписывай
структуру; расширяй существующую. При архитектурных решениях учитывай
расширяемость, тестируемость и долгосрочную поддерживаемость; объясняй
trade-offs; предлагай альтернативы; не давай «быстрых хаков» без объяснения
рисков; избегай антипаттернов.

## Что это за проект

CLI-утилита (**.NET 10**, `System.CommandLine`) **и** MCP-сервер (stdio) для
двусторонней синхронизации дерева страниц Confluence с локальными папками.
Git-подобная модель: `update` (force) и `merge` (smart) с детектом конфликтов.
Работает одинаково на Confluence **Server/DC и Cloud**.

Реальные команды CLI: `download update`, `download merge`, `upload update`,
`upload create`, `upload merge`, `compare`, `config show`. (Названий
`export`/`import`/`sync` в проекте нет — не используй их.)

## Локальный формат зеркала

Одна папка = одна страница. `index.html` = тело в **Confluence Storage Format**
(`body.storage.value`). Маркер `.id<pageId>_<version>` (в теле — JSON
`{title, space}`) = стабильная идентификация страницы + версия на сервере +
точка отсчёта конфликта (`LastWriteTimeUtc`). Все прочие файлы в папке —
вложения. Полное описание формата — в [`docs/ai-rules/local-mirror-format.mdc`](../../docs/ai-rules/local-mirror-format.mdc)
(тот же файл поставляется пользователям как подключаемое правило — **не путать
его с правилами разработки самой утилиты**).

## КРИТИЧНО: контракт нормализации (детект двойного редактирования)

Это самый важный инвариант всего кода.

- Нормализованный storage format хешируется (SHA-256) и хранится в маркерах
  (`.id*`, JSON-поля `h`/`ne`), чтобы отличить **реальную** локальную правку от
  mtime-only касания (пересохранение в редакторе, pretty-print, копирование,
  `touch`, checkout из VCS).
- **ЛЮБОЕ** изменение нормализации контента — `XmlContentNormalizer` /
  `RegexContentNormalizer` / таблица `HtmlEntities` / правила атрибутов или
  пробелов / алгоритм хеша, либо смена активного `IContentNormalizer` —
  **ОБЯЗАНО** поднять `NormalizationContract.CurrentEpoch` и обновить его
  golden-значение. Иначе хеши, посчитанные по старому рецепту, молча разойдутся
  с новыми; golden-vector тест `ContentHasherTests` падает, пока не обновишь и
  эпоху, и golden.

## Confluence Storage Format

XHTML-подмножество с namespaces `ac:` (макросы), `ri:` (ресурсы), `at:`
(шаблоны). Правила:

- Парсить через `XDocument`/`XmlDocument`, **не** строковыми заменами для
  сложных трансформаций.
- Корректно обрабатывать макросы, таблицы, ссылки (`<ac:link><ri:page>`),
  вложения-картинки (`<ac:image><ri:attachment>`).
- Валидировать XML перед отправкой в API; не генерировать несовместимый HTML.
- Спека: [Cloud](https://developer.atlassian.com/cloud/confluence/storage-format/) ·
  [Server/DC](https://developer.atlassian.com/server/confluence/confluence-storage-format/).

## Confluence REST API

- Обновление страницы: получить текущую версию → `version.number + 1` →
  обработать `409 Conflict`. Тело: `body.storage.value` +
  `representation = "storage"`.
- Явно обрабатывать: `401` / `403` / `404` / `409` / `429` / `5xx`. Учитывать
  пагинацию (`limit`/`start`), версионирование, rate limits.
- **Cloud vs Server.** Cloud автоопределяется по `*.atlassian.net` (или
  `--auth-type cloud`). На Cloud страницы идут через REST **v2** (числовой
  `spaceId` резолвится из ключа автоматически, конфликт версий приходит чистым
  `409`), но upload вложений, CQL и ping живут на **сохранившихся v1-эндпоинтах**
  — v1 content API на Cloud удалён. Клиентские реализации Server/Cloud
  разделены за абстракциями; базовый URL конфигурируется.
- Спека: [Cloud v1](https://developer.atlassian.com/cloud/confluence/rest/v1/) ·
  [Server/DC](https://developer.atlassian.com/server/confluence/confluence-rest-api-examples/).

## MCP-сервер

Утилита запускается как MCP-сервер по stdio (`McpServerRunner`, обёртки в
`Tools/`) — инструменты дублируют операции синка. Инструкции для агентов лежат
в [`docs/mcp/agent-instructions.md`](../../docs/mcp/agent-instructions.md),
встроены в сборку как `EmbeddedResource` и отдаются клиенту через
`InitializeResult.Instructions`. **Меняя поведение MCP-инструментов —
синхронно правь `agent-instructions.md`** (единый источник истины).

Sandbox/безопасность: сервер стартует с `--root-dir` (песочница); путь вне неё
→ `OUT_OF_SANDBOX`. Флаг `--read-only` запрещает `upload`-инструменты
(`READ_ONLY_VIOLATION`). Учитывай оба ограничения при изменении инструментов.

## Стек и конвенции (как в коде — не додумывать)

- **.NET 10**, C# последней версии. `Nullable` + `ImplicitUsings` +
  `TreatWarningsAsErrors=true` — **любой варнинг валит билд**.
- **DI:** `Microsoft.Extensions.Hosting`/`DependencyInjection`; composition root —
  `Infrastructure/ServiceCollectionExtensions.cs`. Сервисы не создавать через
  `new`, абстракции — через интерфейсы.
- **HTTP:** `IHttpClientFactory` (`Microsoft.Extensions.Http`). Retry —
  **кастомный `RetryingHttpHandler : DelegatingHandler`** (экспоненциальный
  backoff, уважает `Retry-After`; POST ретраится только на 429). **Polly не
  используется — не добавляй его.** Не использовать `new HttpClient()`.
- **Логирование:** `Microsoft.Extensions.Logging`, структурное. Никогда не
  логировать токены/секреты.
- **Async:** `async`/`await` + `CancellationToken` сквозным образом. Не
  использовать `.Result`/`.Wait()`.
- **Сериализация:** `Newtonsoft.Json` (подключён в csproj).
- **Конфигурация:** `Microsoft.Extensions.Configuration`, приоритет
  **CLI > env > файл > default**. Секреты не хардкодить.
- **Тесты:** **xunit.v3** на Microsoft Testing Platform + **Moq** + **Shouldly**.
  Это НЕ NUnit, НЕ NSubstitute, НЕ FluentAssertions — не тащи их. HTTP мокать
  кастомным `HttpMessageHandler`; реальные вызовы API в юнит-тестах запрещены.
  Как запускать — см. `CLAUDE.md`.
- **Зависимости:** новые пакеты — только зрелые и поддерживаемые; предпочитать
  `Microsoft.Extensions.*` и то, что уже есть в проекте.

## Релиз и версионирование

Версия — в `Directory.Build.props` (`<Version>`). Релиз — тег `vX.Y.Z`
(GitHub Actions `release.yml` собирает артефакты и включает **оба** README).
CHANGELOG перекатывается `[Unreleased]` → `[X.Y.Z] — YYYY-MM-DD` в обоих языках.

## Поведение при изменениях

1. Держись существующей структуры (`Services/ Infrastructure/ Models/ Options/
   Commands/ Tools/`) — не вводи новые слои без причины.
2. Читай окружающий код и повторяй его идиомы (именование, плотность
   комментариев, стиль).
3. Не ломай архитектурные границы; внешние API — за абстракциями.
4. Сложные места — объясняй; временные компромиссы — только с объяснением риска.
