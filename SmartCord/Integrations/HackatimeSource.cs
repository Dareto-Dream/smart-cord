using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Integrations;

/// <summary>
/// Hackatime (hackatime.hackclub.com). Mirrors mantle's connector: the OAuth
/// token only works against <c>/api/v1/authenticated/projects</c> (all-time
/// totals, so "active now" falls back to a poll-over-poll delta), while a pasted
/// personal API key can hit the WakaTime-protocol <c>statusbar/today</c> endpoint
/// for real per-day hours and language.
/// </summary>
public sealed class HackatimeSource : CodingActivitySource
{
    // Match mantle's port so a redirect URI registered there is reusable.
    private const int Port = 39814;
    private const string WakaProtocolBase = "https://hackatime.hackclub.com/api/hackatime/v1";
    private const string OAuthProjectsUrl = "https://hackatime.hackclub.com/api/v1/authenticated/projects";

    public HackatimeSource(SecretStore secrets, HttpClient http, ILogger logger)
        : base("hackatime", secrets, http, logger)
    {
    }

    public override string DisplayName => "Hackatime";
    protected override string AuthorizeUrl => "https://hackatime.hackclub.com/oauth/authorize";
    protected override string TokenUrl => "https://hackatime.hackclub.com/oauth/token";
    protected override int RedirectPort => Port;
    protected override string Scopes => "read";

    public override string SetupHint =>
        "Register an app at hackatime.hackclub.com/oauth/applications with the redirect URI below, " +
        "then paste the client ID and secret. Or paste your API key from hackatime.hackclub.com/my/settings " +
        "for per-day hours and language.";

    protected override async Task<RawActivity?> FetchAsync(bool oauth, string? bearer, string? apiKey, CancellationToken ct)
    {
        return oauth
            ? await FetchOAuthAsync(bearer!, ct)
            : await FetchWakaProtocolAsync(apiKey ?? "", ct);
    }

    private async Task<RawActivity?> FetchOAuthAsync(string bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OAuthProjectsUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hackatime returned {(int)response.StatusCode}: {Truncate(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? topProject = null;
        double topSeconds = -1;
        double total = 0;
        foreach (var project in projects.EnumerateArray())
        {
            var name = project.TryGetProperty("name", out var n) ? n.GetString() : null;
            var seconds = project.TryGetProperty("total_seconds", out var s) && s.TryGetDouble(out var d) ? d : 0;
            total += seconds;
            if (!string.IsNullOrWhiteSpace(name) && seconds > topSeconds)
            {
                topProject = name;
                topSeconds = seconds;
            }
        }

        // OAuth endpoint is all-time only — no honest "today" number or language.
        return new RawActivity(topProject, Language: null, Editor: null,
            SecondsToday: 0, CumulativeTotalSeconds: total, ReportsActive: null);
    }

    private async Task<RawActivity?> FetchWakaProtocolAsync(string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{WakaProtocolBase}/users/current/statusbar/today");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Hackatime returned {(int)response.StatusCode}: {Truncate(body)}");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var data))
        {
            return null;
        }

        var grandTotal = data.TryGetProperty("grand_total", out var gt) && gt.TryGetProperty("total_seconds", out var gts)
            && gts.TryGetDouble(out var total)
            ? total
            : 0;

        return new RawActivity(
            Project: TopBySeconds(data, "projects"),
            Language: TopBySeconds(data, "languages"),
            Editor: TopBySeconds(data, "editors"),
            SecondsToday: grandTotal,
            CumulativeTotalSeconds: grandTotal,
            ReportsActive: null);
    }
}
