using Newtonsoft.Json;

namespace ConfluencePageExporter.Models;

// Wire models for the Confluence Cloud v2 REST API (/wiki/api/v2).
// Deserialization-only: the Cloud client maps them onto the shared domain
// models (PageData, AttachmentData, …) so the rest of the app never sees
// deployment-specific shapes. All ids are strings in v2 JSON — kept as
// strings, never parsed as numbers.

internal sealed class CloudPage
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("spaceId")]
    public string? SpaceId { get; set; }

    [JsonProperty("parentId")]
    public string? ParentId { get; set; }

    [JsonProperty("version")]
    public CloudVersion? Version { get; set; }

    [JsonProperty("body")]
    public CloudBody? Body { get; set; }

    [JsonProperty("_links")]
    public CloudLinks? Links { get; set; }
}

internal sealed class CloudBody
{
    [JsonProperty("storage")]
    public CloudStorage? Storage { get; set; }
}

internal sealed class CloudStorage
{
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonProperty("representation")]
    public string Representation { get; set; } = "";
}

internal sealed class CloudVersion
{
    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("minorEdit")]
    public bool MinorEdit { get; set; }
}

/// <summary>
/// The <c>_links</c> object of both single entities (<c>webui</c>) and
/// collections (<c>next</c> cursor link).
/// </summary>
internal sealed class CloudLinks
{
    [JsonProperty("webui")]
    public string? WebUi { get; set; }

    [JsonProperty("next")]
    public string? Next { get; set; }
}

internal sealed class CloudResponse<T>
{
    [JsonProperty("results")]
    public List<T> Results { get; set; } = new();

    [JsonProperty("_links")]
    public CloudLinks? Links { get; set; }
}

internal sealed class CloudSpace
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("key")]
    public string Key { get; set; } = "";

    [JsonProperty("name")]
    public string? Name { get; set; }
}

internal sealed class CloudAttachment
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("status")]
    public string? Status { get; set; }

    [JsonProperty("pageId")]
    public string? PageId { get; set; }

    [JsonProperty("mediaType")]
    public string? MediaType { get; set; }

    [JsonProperty("fileSize")]
    public long? FileSize { get; set; }

    [JsonProperty("comment")]
    public string? Comment { get; set; }

    [JsonProperty("downloadLink")]
    public string? DownloadLink { get; set; }

    [JsonProperty("version")]
    public CloudVersion? Version { get; set; }
}
