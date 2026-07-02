namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Confluence deployment flavour the tool talks to. Determines which REST API
/// family the client must use: Server/Data Center (v1, <c>/rest/api</c>) or
/// Cloud (v2, <c>/wiki/api/v2</c>, with a few operations remaining on v1).
/// </summary>
public enum DeploymentType
{
    /// <summary>Server / Data Center.</summary>
    OnPrem,

    /// <summary>Atlassian Cloud (<c>*.atlassian.net</c> or a custom domain).</summary>
    Cloud,
}

/// <summary>
/// Resolves the effective <see cref="DeploymentType"/> from configuration.
/// An explicit <c>--auth-type</c> / <c>Global:AuthType</c> value wins;
/// otherwise the type is auto-detected from the base URL host
/// (<c>*.atlassian.net</c> ⇒ Cloud). Cloud sites served from a custom domain
/// are not detectable by host, so they need the explicit setting.
/// </summary>
public static class DeploymentTypeResolver
{
    public const string OnPremValue = "onprem";
    public const string CloudValue = "cloud";

    private const string CloudHostSuffix = ".atlassian.net";

    /// <summary>
    /// Resolves the deployment type. Throws <see cref="ArgumentException"/>
    /// for an explicit value that is neither <see cref="OnPremValue"/> nor
    /// <see cref="CloudValue"/> — a typo here would otherwise silently select
    /// the wrong API family.
    /// </summary>
    public static DeploymentType Resolve(string? authType, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(authType))
            return Autodetect(baseUrl);

        if (TryParse(authType, out var type))
            return type;

        throw new ArgumentException(
            $"Invalid --auth-type (or Global:AuthType in config) value '{authType}'. " +
            $"Expected '{OnPremValue}' or '{CloudValue}'.");
    }

    /// <summary>Case-insensitive, whitespace-tolerant parse of an explicit value.</summary>
    public static bool TryParse(string? authType, out DeploymentType type)
    {
        switch (authType?.Trim().ToLowerInvariant())
        {
            case OnPremValue:
                type = DeploymentType.OnPrem;
                return true;
            case CloudValue:
                type = DeploymentType.Cloud;
                return true;
            default:
                type = default;
                return false;
        }
    }

    /// <summary>
    /// Detects Cloud by the well-known Atlassian Cloud host suffix. A missing
    /// or unparseable base URL maps to <see cref="DeploymentType.OnPrem"/>
    /// (the historical default) — reporting a missing base URL is the client
    /// registration's job, not this resolver's.
    /// </summary>
    public static DeploymentType Autodetect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return DeploymentType.OnPrem;

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
            return DeploymentType.OnPrem;

        return uri.Host.EndsWith(CloudHostSuffix, StringComparison.OrdinalIgnoreCase)
            ? DeploymentType.Cloud
            : DeploymentType.OnPrem;
    }
}
