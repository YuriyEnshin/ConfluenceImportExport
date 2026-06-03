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

    [JsonProperty("version")]
    public VersionInfo? Version { get; set; }

    [JsonProperty("ancestors")]
    public List<PageAncestor> Ancestors { get; set; } = new();

    [JsonProperty("childTypes")]
    public ChildTypes? ChildTypes { get; set; }

    [JsonProperty("space")]
    public SpaceInfo? Space { get; set; }

    public string? ParentId => Ancestors.Count > 0 ? Ancestors[^1].Id : null;

    /// <summary>
    /// Space key the page belongs to. Populated when the API response was
    /// requested with <c>expand=space</c>; null otherwise. A Confluence page
    /// tree always lives in a single space, so the server value is the
    /// authority for where a page (and its descendants) reside.
    /// </summary>
    public string? SpaceKey => Space?.Key;
}

public class ChildTypes
{
    [JsonProperty("page")]
    public ChildTypeFlag? Page { get; set; }

    [JsonProperty("attachment")]
    public ChildTypeFlag? Attachment { get; set; }

    public bool HasPages => Page?.Value ?? true;
    public bool HasAttachments => Attachment?.Value ?? true;
}

public class ChildTypeFlag
{
    [JsonProperty("value")]
    public bool Value { get; set; }
}

public class SpaceInfo
{
    [JsonProperty("key")]
    public string Key { get; set; } = "";
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
