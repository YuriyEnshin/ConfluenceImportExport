using System.CommandLine;
using System.CommandLine.Parsing;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public static class CommandValueResolver
{
    public static string ResolveRequiredPath(ParseResult parseResult, Option<string> option, string? configValue, string optionName)
    {
        var value = ResolveOptionalPath(parseResult, option, configValue);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required parameter: {optionName}");
        return value;
    }

    public static string? ResolveOptionalPath(ParseResult parseResult, Option<string> option, string? configValue)
    {
        var value = ResolveOptionalString(parseResult, option, configValue);
        return NormalizePathInput(value);
    }

    public static string ResolveRequiredString(ParseResult parseResult, Option<string> option, string? configValue, string optionName)
    {
        var value = ResolveOptionalString(parseResult, option, configValue);
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing required parameter: {optionName}");
        return value;
    }

    public static string? ResolveOptionalString(ParseResult parseResult, Option<string> option, string? configValue)
    {
        if (IsCliOptionSpecified(parseResult, option))
            return parseResult.GetValue(option);

        return configValue;
    }

    public static string ResolveEnum(
        ParseResult parseResult,
        Option<string> option,
        string? configValue,
        string fallback,
        string optionName,
        params string[] allowedValues)
    {
        var value = ResolveOptionalString(parseResult, option, configValue) ?? fallback;
        if (!allowedValues.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid value '{value}' for {optionName}. Allowed values: {string.Join(", ", allowedValues)}");
        }

        return value;
    }

    public static bool ResolveBool(ParseResult parseResult, Option<bool> option, bool? configValue, bool fallback = false)
    {
        if (IsCliOptionSpecified(parseResult, option))
            return parseResult.GetValue(option);

        return configValue ?? fallback;
    }

    public static bool IsCliOptionSpecified<T>(ParseResult parseResult, Option<T> option)
    {
        return parseResult.CommandResult.Children
            .OfType<OptionResult>()
            .Any(r => ReferenceEquals(r.Option, option));
    }

    public static string? ExtractConfigPathFromArgs(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Option --config requires a file path.");
                return NormalizePathInput(args[i + 1]);
            }

            const string configPrefix = "--config=";
            if (arg.StartsWith(configPrefix, StringComparison.OrdinalIgnoreCase))
                return NormalizePathInput(arg[configPrefix.Length..]);
        }

        return null;
    }

    public static string? NormalizePathInput(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var normalized = value.Trim();

        if (normalized.Length >= 2)
        {
            var first = normalized[0];
            var last = normalized[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                normalized = normalized[1..^1];
            }
        }

        // Support escaped spaces often used in unix shell contexts (e.g. My\ New\ Project)
        normalized = normalized.Replace("\\ ", " ", StringComparison.Ordinal);

        return normalized;
    }
}
