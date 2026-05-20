namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Shared validation helpers for command-line / MCP tool arguments.
/// Throws <see cref="ArgumentException"/> with messages suitable for both
/// CLI users and MCP clients (callers may translate to domain exceptions).
/// </summary>
public static class ArgValidation
{
    /// <summary>
    /// Asserts that exactly one of the named arguments has a non-empty value.
    /// </summary>
    public static void RequireExactlyOne(params (string Name, string? Value)[] args)
    {
        var present = args.Where(a => !string.IsNullOrEmpty(a.Value)).ToArray();
        if (present.Length == 0)
        {
            var names = string.Join(" or ", args.Select(a => a.Name));
            throw new ArgumentException($"One of {names} must be specified.");
        }
        if (present.Length > 1)
        {
            var names = string.Join(" and ", present.Select(a => a.Name));
            throw new ArgumentException($"{names} are mutually exclusive. Specify only one.");
        }
    }

    /// <summary>
    /// Asserts that at most one of the named arguments has a non-empty value.
    /// </summary>
    public static void RequireAtMostOne(params (string Name, string? Value)[] args)
    {
        var present = args.Where(a => !string.IsNullOrEmpty(a.Value)).ToArray();
        if (present.Length > 1)
        {
            var names = string.Join(" and ", present.Select(a => a.Name));
            throw new ArgumentException($"{names} are mutually exclusive. Specify only one.");
        }
    }
}
