Нормальный путь для такой утилиты — **разделить две задачи**:

1. **System.CommandLine** отвечает только за форму CLI: команды, опции, help, валидацию, parse/result.
2. **Microsoft.Extensions.Configuration + Generic Host + DI** отвечают за итоговую конфигурацию приложения: JSON/INI/env/секреты/command line и привязку к strongly-typed options. В .NET именно этот стек является типовым способом собирать конфигурацию из нескольких провайдеров и запускать консольные приложения через host. ([Microsoft Learn][1])

Я бы делал так:

## Главная идея

Для каждой команды есть свой класс настроек, например `ImportOptions`, `ExportOptions`, `SyncOptions`.

Итоговые значения берутся из источников в таком приоритете:

**config file < env vars < CLI**

Это хорошо ложится на стандартную модель конфигурации .NET, где позднее добавленный provider перекрывает ранний. Командная строка может быть подключена как `CommandLineConfigurationProvider`, а переменные окружения — как `EnvironmentVariablesConfigurationProvider`. ([Microsoft Learn][2])

Но есть важный нюанс:

## Не стоит пытаться сделать System.CommandLine единственным источником значений

Если одни и те же опции одновременно живут и в `System.CommandLine`, и в `IConfiguration`, легко получить дублирование логики.

Лучше использовать такую схему:

* `System.CommandLine`:

  * определяет команды и опции;
  * знает aliases, help, required-правила, кастомную валидацию;
  * умеет распарсить `args`;
  * отдельно получает только “инфраструктурные” параметры первого этапа, например `--config`, `--environment`, `--verbose`.

* `IConfiguration`:

  * собирает итоговый набор значений для конкретной команды;
  * связывается с POCO через binder;
  * используется сервисами приложения.

То есть CLI у тебя остается красивым и декларативным, а конфиг — единым и переиспользуемым.

---

# Рекомендуемая архитектура

## 1) Двухфазный запуск

### Фаза 1. Минимальный parse

Сначала очень легким деревом `System.CommandLine` выясняешь:

* какая команда вызвана;
* где лежит config-файл (`--config`);
* какой environment выбран;
* может быть, уровень логирования.

Это нужно, потому что путь к конфигу сам влияет на сборку `IConfiguration`.

### Фаза 2. Сборка Host и полной конфигурации

Дальше:

* строишь `HostApplicationBuilder` / Generic Host;
* очищаешь дефолтные источники конфигурации, если хочешь полный контроль;
* добавляешь:

  * базовый `appsettings.json`;
  * `appsettings.{Environment}.json`;
  * командный файл, например `mytool.json`;
  * env vars с префиксом, например `MYTOOL_`;
  * **только релевантные CLI-аргументы** как command-line configuration provider.

Generic Host в .NET предназначен для startup, lifetime management, DI, logging и configuration, и он вполне подходит для console apps. ([Microsoft Learn][3])

---

# 2) Иерархия ключей по имени команды

Очень удобно хранить конфиг не плоско, а по секциям команд.

Например:

```json
{
  "Global": {
    "Verbose": true
  },
  "Import": {
    "Input": "data/input.csv",
    "BatchSize": 500,
    "DryRun": false
  },
  "Export": {
    "Output": "data/out.json",
    "Format": "json"
  }
}
```

Env-переменные:

```bash
MYTOOL__GLOBAL__VERBOSE=true
MYTOOL__IMPORT__INPUT=/tmp/in.csv
MYTOOL__IMPORT__BATCHSIZE=1000
```

В конфигурации .NET иерархия ключей поддерживается через `:`; для env-переменных обычно используют `__`. ([Microsoft Learn][2])

---

# 3) Привязка к strongly typed options

Для каждой команды:

```csharp
public sealed class ImportOptions
{
    public string Input { get; set; } = "";
    public int BatchSize { get; set; } = 100;
    public bool DryRun { get; set; }
}
```

И затем:

```csharp
services
    .AddOptions<ImportOptions>()
    .Bind(configuration.GetSection("Import"))
    .ValidateDataAnnotations()
    .Validate(o => o.BatchSize > 0, "BatchSize must be > 0");
```

Так ты получаешь:

* единый механизм значений;
* валидацию на уровне options;
* удобный DI.

---

# 4) Командная строка должна перекрывать только реально заданные опции

Это ключевой момент.

Если просто передать все `args` в `AddCommandLine(args)`, то в конфигурацию попадут только явно переданные значения — и это как раз хорошо. Такой provider берет ключи из аргументов командной строки. ([Microsoft Learn][4])

Но `System.CommandLine` и `CommandLineConfigurationProvider` понимают CLI немного по-разному. Поэтому я бы не пускал “сырые args” в конфиг напрямую для сложного CLI с командами и алиасами, а делал **нормализацию**:

* `System.CommandLine` парсит args;
* из `ParseResult` ты извлекаешь только опции, которые пользователь **явно указал**;
* переводишь их в конфигурационные ключи вида:

  * `Global:Verbose=true`
  * `Import:Input=...`
  * `Import:BatchSize=...`
* добавляешь это как `AddInMemoryCollection(...)`.

Это надежнее, чем надеяться, что формат CLI и формат provider всегда совпадут.

Итоговый порядок провайдеров:

```csharp
config
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{env}.json", optional: true)
    .AddJsonFile(configPath, optional: true)
    .AddEnvironmentVariables(prefix: "MYTOOL__")
    .AddInMemoryCollection(cliOverrides);
```

Поскольку последний provider имеет высший приоритет, CLI корректно переопределит env и файл. Порядок источников влияет на приоритет значений. ([Microsoft Learn][2])

---

# 5) Команды как сервисы

Каждую команду лучше реализовать отдельным handler/service:

```csharp
public interface ICommandHandler<TOptions>
{
    Task<int> ExecuteAsync(TOptions options, CancellationToken ct);
}
```

Пример:

```csharp
public sealed class ImportCommandHandler : ICommandHandler<ImportOptions>
{
    private readonly ILogger<ImportCommandHandler> _logger;
    private readonly IImporter _importer;

    public ImportCommandHandler(
        ILogger<ImportCommandHandler> logger,
        IImporter importer)
    {
        _logger = logger;
        _importer = importer;
    }

    public async Task<int> ExecuteAsync(ImportOptions options, CancellationToken ct)
    {
        _logger.LogInformation("Import started: {Input}", options.Input);
        await _importer.RunAsync(options, ct);
        return 0;
    }
}
```

Тогда команда в CLI просто резолвит нужный сервис из DI и вызывает его.

---

# Практический шаблон

## Program.cs

Ниже не “готовый фреймворк”, а опорная схема.

```csharp
using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var root = BuildRootCommand();
var parseResult = root.Parse(args);

if (parseResult.Errors.Count > 0)
{
    return await parseResult.InvokeAsync();
}

// 1. Определяем инфраструктурные значения ранней стадии
var boot = BootstrapSettings.FromParseResult(parseResult);

// 2. Собираем CLI override-ключи в конфигурационном формате
var cliOverrides = CliOverrideBuilder.Build(parseResult);

// 3. Поднимаем host
var builder = Host.CreateApplicationBuilder();

builder.Configuration.Sources.Clear();

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{boot.Environment}.json", optional: true, reloadOnChange: false);

if (!string.IsNullOrWhiteSpace(boot.ConfigPath))
{
    builder.Configuration.AddJsonFile(boot.ConfigPath, optional: false, reloadOnChange: false);
}

builder.Configuration
    .AddEnvironmentVariables(prefix: "MYTOOL__")
    .AddInMemoryCollection(cliOverrides);

builder.Services.AddLogging(logging =>
{
    logging.AddSimpleConsole();
});

builder.Services.AddTransient<ImportCommandHandler>();
builder.Services.AddTransient<ExportCommandHandler>();

builder.Services
    .AddOptions<ImportOptions>()
    .Bind(builder.Configuration.GetSection("Import"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Input), "Import:Input is required")
    .Validate(o => o.BatchSize > 0, "Import:BatchSize must be > 0");

builder.Services
    .AddOptions<ExportOptions>()
    .Bind(builder.Configuration.GetSection("Export"));

using var host = builder.Build();

// 4. Диспетчеризация команды
return await CommandDispatcher.DispatchAsync(parseResult, host.Services);
```

---

## Определение CLI

```csharp
static RootCommand BuildRootCommand()
{
    var configOption = new Option<string?>("--config", "Path to config file");
    var environmentOption = new Option<string?>("--environment", () => "Production");
    var verboseOption = new Option<bool>("--verbose");

    var import = new Command("import", "Import data");
    var input = new Option<string?>("--input");
    var batchSize = new Option<int?>("--batch-size");
    var dryRun = new Option<bool?>("--dry-run");

    import.AddOption(input);
    import.AddOption(batchSize);
    import.AddOption(dryRun);

    var export = new Command("export", "Export data");
    var output = new Option<string?>("--output");
    var format = new Option<string?>("--format");

    export.AddOption(output);
    export.AddOption(format);

    var root = new RootCommand("MyTool");
    root.AddGlobalOption(configOption);
    root.AddGlobalOption(environmentOption);
    root.AddGlobalOption(verboseOption);

    root.AddCommand(import);
    root.AddCommand(export);

    return root;
}
```

`System.CommandLine` как раз разделяет parsing и invocation, так что сначала получить `ParseResult`, а потом самостоятельно решить, как строить host и запускать обработчик — это нормальная модель. ([Microsoft Learn][1])

---

## Построение CLI overrides

```csharp
using System.CommandLine;
using System.CommandLine.Parsing;

public static class CliOverrideBuilder
{
    public static IReadOnlyDictionary<string, string?> Build(ParseResult parseResult)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // глобальные
        AddIfExplicit(parseResult, "--verbose", "Global:Verbose", result);
        AddIfExplicit(parseResult, "--environment", "Global:Environment", result);

        var commandName = parseResult.CommandResult.Command.Name;

        switch (commandName)
        {
            case "import":
                AddIfExplicit(parseResult, "--input", "Import:Input", result);
                AddIfExplicit(parseResult, "--batch-size", "Import:BatchSize", result);
                AddIfExplicit(parseResult, "--dry-run", "Import:DryRun", result);
                break;

            case "export":
                AddIfExplicit(parseResult, "--output", "Export:Output", result);
                AddIfExplicit(parseResult, "--format", "Export:Format", result);
                break;
        }

        return result;
    }

    private static void AddIfExplicit(
        ParseResult parseResult,
        string optionName,
        string configKey,
        IDictionary<string, string?> dict)
    {
        var option = FindOption(parseResult, optionName);
        if (option is null)
            return;

        var optionResult = parseResult.CommandResult
            .Children
            .OfType<OptionResult>()
            .FirstOrDefault(x => x.Option == option);

        if (optionResult is null)
            return;

        // Важно: кладем только если опция была реально передана пользователем
        if (optionResult.Tokens.Count == 0 && option.Arity.MaximumNumberOfValues != 0)
            return;

        var value = parseResult.GetValue(option);
        dict[configKey] = value?.ToString();
    }

    private static Option? FindOption(ParseResult parseResult, string name)
    {
        return parseResult.CommandResult.Command.Options
            .FirstOrDefault(o => o.Name == name.TrimStart('-') || o.Aliases.Contains(name));
    }
}
```

Тут идея важнее конкретного API: в in-memory configuration надо класть только те параметры, которые были заданы явно, иначе ты случайно затрешь значения из файла/окружения дефолтами CLI.

---

# Почему не `System.Configuration`

Если речь о современном .NET console app, то типовой путь — это **не** `System.Configuration`, а `Microsoft.Extensions.Configuration`. Именно он поддерживает стандартных providers, binder, options pattern и естественно встраивается в Generic Host. Документация .NET по конфигурации и Generic Host описывает именно этот стек. ([Microsoft Learn][3])

`System.Configuration` имеет смысл в основном для legacy-сценариев и старого `App.config`/`.config`. Для нового CLI-инструмента на .NET я бы его не выбирал.

---

# Что я бы рекомендовал как “best practice”

## Вариант A — лучший

**System.CommandLine + Generic Host + Microsoft.Extensions.Configuration + Options**

Подходит почти всегда.

Плюсы:

* стандартный стек .NET;
* хорошая тестируемость;
* DI/logging/options из коробки;
* прозрачный приоритет источников.

## Вариант B — если очень хочется минимализма

Вообще не строить host до момента вызова команды, а собирать `ConfigurationBuilder` вручную на каждую команду.

Подходит для очень маленьких утилит, но хуже масштабируется.

---

# Дополнительные рекомендации

## 1. Разделяй глобальные и командные настройки

Например:

* `Global:*`
* `Import:*`
* `Export:*`

Так и env, и JSON, и документация CLI будут понятнее.

## 2. Используй единый словарь имен

Не пытайся автоматически маппить все имена “как получится”.
Явный mapping лучше:

* `--batch-size` -> `Import:BatchSize`
* `--dry-run` -> `Import:DryRun`

Это снимает массу сюрпризов.

## 3. Валидацию делай на двух уровнях

* синтаксическую — в `System.CommandLine`;
* семантическую — в options validation.

Например:

* CLI проверяет, что `--format` входит в список значений;
* options validation проверяет, что `Output` обязателен при `Format=json`.

## 4. Для complex object не пытайся все задавать CLI

CLI хорошо подходит для скаляров и коротких коллекций.
Сложные структуры лучше задавать JSON-конфигом.

## 5. Делай явный вывод “effective configuration”

Команда вроде `config show` или лог на старте:

* что взято из файла,
* что из env,
* что overridden CLI.

Это очень помогает при эксплуатации.

---

# Итог

Самая грамотная реализация в современном .NET выглядит так:

* **System.CommandLine** — описывает команды/опции и парсит `args`;
* **двухфазный запуск** — сначала bootstrap parse, потом сборка полноценного host;
* **Microsoft.Extensions.Configuration** — собирает значения из JSON/env/CLI override;
* **Microsoft.Extensions.Hosting** — host, DI, logging, lifecycle;
* **Options pattern** — strongly typed настройки на каждую команду;
* **приоритет источников** — config < env < CLI.

И отдельно: я бы **не строил решение вокруг `System.Configuration`**, а сразу делал его на `Microsoft.Extensions.Configuration`, потому что это стандартный современный механизм конфигурации для .NET-приложений и он лучше сочетается с Host/DI/Options. ([Microsoft Learn][3])

Могу сразу дать тебе следующий шаг: готовый минимальный каркас проекта с `Program.cs`, одной командой `import`, JSON-конфигом и env/CLI override.

[1]: https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-parse-and-invoke?utm_source=chatgpt.com "How to parse and invoke the result - .NET"
[2]: https://learn.microsoft.com/en-us/dotnet/core/extensions/configuration?utm_source=chatgpt.com "Configuration - .NET"
[3]: https://learn.microsoft.com/en-us/dotnet/core/extensions/generic-host?utm_source=chatgpt.com "NET Generic Host"
[4]: https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.configuration.commandline.commandlineconfigurationprovider?view=net-10.0-pp&utm_source=chatgpt.com "CommandLineConfigurationProv..."
