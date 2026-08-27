using System.Net.Http.Json;
using System.Text.Json;

namespace SmartCord;

public sealed class ProjectApiClient(HttpClient httpClient, SmartCordSettings settings)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<ProjectItem>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        await using var stream = await httpClient.GetStreamAsync(settings.Api.ProjectsUrl, cancellationToken);
        var projects = await ProjectJson.ReadProjectsAsync(stream, SerializerOptions, cancellationToken);

        return projects
            .Where(project => !string.IsNullOrWhiteSpace(project.Title))
            .OrderBy(project => project.Title)
            .ToList();
    }
}
