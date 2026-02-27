using Newtonsoft.Json;

namespace ConfluencePageExporter.Models;

public class PageData
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";

    [JsonProperty("_links")]
    public Links Links { get; set; } = new();

    [JsonProperty("body")]
    public Body Body { get; set; } = new();

    [JsonProperty("ancestors")]
    public List<PageAncestor> Ancestors { get; set; } = new();

    public string? ParentId => Ancestors.Count > 0 ? Ancestors[^1].Id : null;
}

public class PageAncestor
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("title")]
    public string Title { get; set; } = "";
}

public class Links
{
    [JsonProperty("webui")]
    public string WebUi { get; set; } = "";
}

public class Body
{
    [JsonProperty("storage")]
    public StorageContent Storage { get; set; } = new();
}

public class StorageContent
{
    [JsonProperty("value")]
    public string Value { get; set; } = "";

    [JsonProperty("representation")]
    public string Representation { get; set; } = "";
}
