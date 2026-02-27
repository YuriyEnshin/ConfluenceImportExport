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

    [JsonProperty("_links")]
    public AttachmentLinks Links { get; set; } = new();
}

public class AttachmentLinks
{
    [JsonProperty("download")]
    public string DownloadUrl { get; set; } = "";
}
