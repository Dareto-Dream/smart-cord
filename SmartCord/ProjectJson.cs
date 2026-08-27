using System.Text.Json;

namespace SmartCord;

public static class ProjectJson
{
    public static async Task<List<ProjectItem>> ReadProjectsAsync(
        Stream stream,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ProjectItem>>(root.GetRawText(), options) ?? [];
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ProjectItem>>(value.GetRawText(), options) ?? [];
        }

        return [];
    }
}
