namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Normalizes path strings from CLI/config: trims whitespace, strips wrapping quotes,
/// and unescapes backslash-space sequences common in unix shells.
/// </summary>
public static class PathNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var normalized = value.Trim();

        if (normalized.Length >= 2)
        {
            var first = normalized[0];
            var last = normalized[^1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                normalized = normalized[1..^1];
        }

        normalized = normalized.Replace("\\ ", " ", StringComparison.Ordinal);

        return normalized;
    }
}
