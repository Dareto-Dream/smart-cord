using System.Text.Json.Serialization;

namespace SmartCord;

public sealed class ProjectResponse
{
    [JsonPropertyName("value")]
    public List<ProjectItem> Value { get; set; } = [];

    [JsonPropertyName("Count")]
    public int Count { get; set; }
}

public sealed class ProjectItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("progress")]
    public string Progress { get; set; } = "";

    [JsonPropertyName("thumbnail_url")]
    public string ThumbnailUrl { get; set; } = "";

    [JsonPropertyName("link")]
    public string Link { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("downloads")]
    public List<ProjectLink> Downloads { get; set; } = [];

    [JsonPropertyName("videos")]
    public List<ProjectLink> Videos { get; set; } = [];

    [JsonPropertyName("iframes")]
    public List<ProjectLink> Iframes { get; set; } = [];

    [JsonPropertyName("metadata")]
    public ProjectMetadata Metadata { get; set; } = new();
}

public sealed class ProjectLink
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public sealed class ProjectMetadata
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("maturity")]
    public string Maturity { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("disciplines")]
    public List<string> Disciplines { get; set; } = [];

    [JsonPropertyName("platforms")]
    public List<string> Platforms { get; set; } = [];

    [JsonPropertyName("themes")]
    public List<string> Themes { get; set; } = [];
}
