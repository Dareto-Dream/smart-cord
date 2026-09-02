using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Integrations;

/// <summary>
/// WakaTime (wakatime.com by default; the API base is overridable for a
/// self-hosted Wakapi). OAuth uses the authorization-code flow against
/// wakatime.com; the pasted-key mode sends HTTP Basic with the API key, which
/// also works against Wakapi.
/// </summary>
public sealed class WakaTimeSource : CodingActivitySource
{
    private const int Port = 39820;

    private string _apiBase;

    public WakaTimeSource(SecretStore secrets, HttpClient http, ILogger logger, string apiBase)
        : base("wakatime", secrets, http, logger)
    {
        _apiBase = Normalize(apiBase);
    }

    public override string DisplayName => "WakaTime";
    protected override string AuthorizeUrl => "https://wakatime.com/oauth/authorize";
    protected override string TokenUrl => "https://wakatime.com/oauth/token";
    protected override int RedirectPort => Port;
    protected override string Scopes => "read_summaries,read_stats";

    public override string SetupHint =>
        "Create an app at wakatime.com/apps, set the redirect URI to the one below, then paste " +
        "the App ID (client ID) and App Secret. Or skip OAuth and paste an API key from " +
        "wakatime.com/settings/account.";

    public void SetApiBase(string apiBase)
    {
        _apiBase = Normalize(apiBase);
    }

    protected override async Task<RawActivity?> FetchAsync(bool oauth, string? bearer, string? apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_apiBase}/users/current/status_bar/today");
        if (oauth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        else
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(apiKey ?? ""));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        using var response = await Http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"WakaTime returned {(int)response.StatusCode}: {Truncate(body)}");
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

    private static string Normalize(string apiBase)
    {
        var value = string.IsNullOrWhiteSpace(apiBase) ? "https://wakatime.com/api/v1" : apiBase.Trim();
        return value.TrimEnd('/');
    }
}
