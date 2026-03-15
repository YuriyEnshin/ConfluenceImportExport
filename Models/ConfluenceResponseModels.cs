using Newtonsoft.Json;

namespace ConfluencePageExporter.Models;

/// <summary>
/// Response wrapper for Confluence API paginated results
/// </summary>
public class ConfluenceResponse<T>
{
    [JsonProperty("results")]
    public List<T> Results { get; set; } = new();

    [JsonProperty("_links")]
    public ResponseLinks? Links { get; set; }

    [JsonProperty("size")]
    public int Size { get; set; }

    [JsonProperty("limit")]
    public int Limit { get; set; }

    [JsonProperty("start")]
    public int Start { get; set; }
}

public class ResponseLinks
{
    [JsonProperty("next")]
    public string? Next { get; set; }

    [JsonProperty("base")]
    public string? Base { get; set; }

    [JsonProperty("context")]
    public string? Context { get; set; }
}

/// <summary>
/// Response for page creation/update
/// </summary>
public class PageResponse
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("version")]
    public VersionInfo? Version { get; set; }
}

public class VersionInfo
{
    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("when")]
    public DateTime? When { get; set; }

    [JsonProperty("minorEdit")]
    public bool MinorEdit { get; set; }
}

public class PageVersionSummary
{
    [JsonProperty("number")]
    public int Number { get; set; }

    [JsonProperty("when")]
    public DateTime? When { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    [JsonProperty("minorEdit")]
    public bool MinorEdit { get; set; }
}
