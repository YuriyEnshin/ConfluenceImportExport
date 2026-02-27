using System.CommandLine;

namespace ConfluencePageExporter.Commands;

/// <summary>
/// Extension methods for creating command options with common patterns
/// </summary>
public static class CommandOptionsBuilder
{
    public static Option<string> CreateRequiredStringOption(string name, string description)
    {
        var option = new Option<string>(name);
        option.Description = description;
        option.Required = true;
        return option;
    }

    public static Option<string> CreateOptionalStringOption(string name, string description)
    {
        var option = new Option<string>(name);
        option.Description = description;
        option.Required = false;
        return option;
    }

    public static Option<string> CreateEnumOption(string name, string description, params string[] allowedValues)
    {
        var option = new Option<string>(name);
        option.Description = description;
        option.Required = false;
        option.AcceptOnlyFromAmong(allowedValues);
        return option;
    }

    public static Option<bool> CreateBoolOption(string name, string description)
    {
        var option = new Option<bool>(name);
        option.Description = description;
        return option;
    }
}
