using Newtonsoft.Json;

namespace ConfluencePageExporter.Models;

public class AttachmentData
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("mediaType")]
    public string MediaType { get; set; } = "";

    [JsonProperty("extensions")]
    public AttachmentExtensions? Extensions { get; set; }

    [JsonProperty("version")]
    public VersionInfo? Version { get; set; }

    [JsonProperty("_links")]
    public AttachmentLinks Links { get; set; } = new();

    /// <summary>
    /// The attachment's server-side media type, preferring <c>extensions.mediaType</c>
    /// (populated by <c>expand=extensions</c>) over the top-level field, or
    /// <c>null</c> when neither is known. Used on data-update so the part's
    /// Content-Type matches the stored type — Confluence rejects a media-type
    /// change on update, which silently drops updates to extensionless
    /// attachments whose type the server would otherwise re-infer from a
    /// (missing) extension.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveMediaType =>
        !string.IsNullOrWhiteSpace(Extensions?.MediaType) ? Extensions!.MediaType
        : !string.IsNullOrWhiteSpace(MediaType) ? MediaType
        : null;
}

public class AttachmentExtensions
{
    [JsonProperty("fileSize")]
    public long? FileSize { get; set; }

    [JsonProperty("mediaType")]
    public string? MediaType { get; set; }

    [JsonProperty("comment")]
    public string? Comment { get; set; }
}

public class AttachmentLinks
{
    [JsonProperty("download")]
    public string DownloadUrl { get; set; } = "";
}
