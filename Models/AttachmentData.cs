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
