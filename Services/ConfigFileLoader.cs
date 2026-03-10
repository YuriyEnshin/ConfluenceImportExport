using System.Text.Json;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public static class ConfigFileLoader
{
    public static AppConfig Load(string? explicitConfigPath, string defaultConfigFileName = "confluence-exporter.json")
    {
        var hasExplicitPath = !string.IsNullOrWhiteSpace(explicitConfigPath);
        var configPath = hasExplicitPath
            ? Path.GetFullPath(explicitConfigPath!)
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), defaultConfigFileName));

        if (!File.Exists(configPath))
        {
            if (hasExplicitPath)
                throw new FileNotFoundException($"Configuration file does not exist: {configPath}");

            return new AppConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<AppConfig>(json, options) ?? new AppConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Configuration file is invalid: {configPath}. {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Cannot read configuration file: {configPath}. {ex.Message}");
        }
    }
}
