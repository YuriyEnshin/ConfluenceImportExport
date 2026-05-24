namespace ConfluencePageExporter.Models;

/// <summary>
/// Subset of fields returned by the <c>/rest/api/user/current</c> Confluence
/// endpoint, used only by the diagnostic ping tool. Both on-prem and cloud
/// surface <see cref="Username"/> and <see cref="DisplayName"/>; the rest are
/// best-effort and may be <c>null</c> across deployments.
/// </summary>
public sealed class ConfluenceUser
{
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public string? UserKey { get; set; }
    public string? AccountId { get; set; }
    public string? Type { get; set; }
}
