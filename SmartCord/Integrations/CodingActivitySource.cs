using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Integrations;

public enum ConnectionKind
{
    None,
    ApiKey,
    OAuthPending,
    OAuth,
}

/// <summary>
/// Shared plumbing for a WakaTime-protocol time-tracking source: OAuth loopback
/// connect, token refresh, pasted-API-key fallback, and turning a raw poll into a
/// <see cref="CodingSnapshot"/> with an "active right now" flag. Subclasses supply
/// the endpoints and the response parsing.
/// </summary>
public abstract class CodingActivitySource
{
    private readonly SecretStore _secrets;
    private readonly HttpClient _http;
    protected readonly ILogger Logger;

    private double? _lastTotal;

    protected CodingActivitySource(string key, SecretStore secrets, HttpClient http, ILogger logger)
    {
        Key = key;
        _secrets = secrets;
        _http = http;
        Logger = logger;
        _lastTotal = double.TryParse(_secrets.Get($"{key}.last_total"), out var t) ? t : null;
    }

    /// <summary>Stable short key used for secret storage and asset keys ("wakatime" / "hackatime").</summary>
    public string Key { get; }

    public abstract string DisplayName { get; }
    protected abstract string AuthorizeUrl { get; }
    protected abstract string TokenUrl { get; }
    protected abstract int RedirectPort { get; }
    protected abstract string Scopes { get; }

    /// <summary>Registration page + notes shown in the Integrations UI.</summary>
    public abstract string SetupHint { get; }

    public string RedirectUri => $"http://127.0.0.1:{RedirectPort}/callback";

    public CodingSnapshot? Latest { get; private set; }

    public ConnectionKind Connection
    {
        get
        {
            if (_secrets.Has($"{Key}.access_token"))
            {
                return ConnectionKind.OAuth;
            }
            if (_secrets.Has($"{Key}.client_id") && _secrets.Has($"{Key}.client_secret"))
            {
                return ConnectionKind.OAuthPending;
            }
            return _secrets.Has($"{Key}.api_key") ? ConnectionKind.ApiKey : ConnectionKind.None;
        }
    }

    public bool IsConnected => Connection is ConnectionKind.OAuth or ConnectionKind.ApiKey;

    public string? PendingClientId =>
        Connection == ConnectionKind.OAuthPending ? _secrets.Get($"{Key}.client_id") : null;

    public string StatusLine => Connection switch
    {
        ConnectionKind.OAuth => "Connected via OAuth",
        ConnectionKind.ApiKey => "Connected with an API key",
        ConnectionKind.OAuthPending => "Client saved — finish connecting",
        _ => "Not connected",
    };

    // ── configuration ──────────────────────────────────────────────────

    public void SetApiKey(string? apiKey)
    {
        _secrets.Set($"{Key}.api_key", string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
        Latest = null;
    }

    public void SaveOAuthClient(string clientId, string clientSecret)
    {
        _secrets.Set($"{Key}.client_id", clientId.Trim());
        _secrets.Set($"{Key}.client_secret", clientSecret.Trim());
    }

    /// <summary>Removes OAuth material but keeps any pasted API key.</summary>
    public void DisconnectOAuth()
    {
        _secrets.Set($"{Key}.access_token", null);
        _secrets.Set($"{Key}.refresh_token", null);
        _secrets.Set($"{Key}.expires_at", null);
        _secrets.Set($"{Key}.client_id", null);
        _secrets.Set($"{Key}.client_secret", null);
        Latest = null;
    }

    public void DisconnectAll()
    {
        _secrets.RemoveByPrefix($"{Key}.");
        _lastTotal = null;
        Latest = null;
    }

    // ── OAuth ──────────────────────────────────────────────────────────

    public async Task ConnectOAuthAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        SaveOAuthClient(clientId, clientSecret);
        await FinishPendingOAuthAsync(ct);
    }

    public async Task FinishPendingOAuthAsync(CancellationToken ct)
    {
        var clientId = _secrets.Get($"{Key}.client_id")
                       ?? throw new InvalidOperationException("No saved client ID to connect with.");
        var clientSecret = _secrets.Get($"{Key}.client_secret")
                           ?? throw new InvalidOperationException("No saved client secret to connect with.");

        var authorizeUrl =
            $"{AuthorizeUrl}?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&scope={Uri.EscapeDataString(Scopes)}";

        var code = await OAuthLoopbackFlow.AuthorizeAsync(
            authorizeUrl, RedirectPort, "/callback", TimeSpan.FromMinutes(5), ct);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        };

        var token = await PostTokenAsync(form, ct);
        StoreToken(token);
        Logger.LogInformation("{Source} OAuth connected", DisplayName);
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token request failed ({(int)response.StatusCode}): {Truncate(body)}");
        }

        return TokenResponse.Parse(body);
    }

    private void StoreToken(TokenResponse token)
    {
        _secrets.Set($"{Key}.access_token", token.AccessToken);
        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            _secrets.Set($"{Key}.refresh_token", token.RefreshToken);
        }
        _secrets.Set($"{Key}.expires_at", token.ExpiresInSeconds is { } s
            ? DateTimeOffset.UtcNow.AddSeconds(s).ToString("o")
            : null);
    }

    private async Task<string> ValidAccessTokenAsync(CancellationToken ct)
    {
        var token = _secrets.Get($"{Key}.access_token")
                    ?? throw new InvalidOperationException("Not connected via OAuth.");

        var expiresAt = _secrets.Get($"{Key}.expires_at");
        var fresh = string.IsNullOrEmpty(expiresAt)
            || (DateTimeOffset.TryParse(expiresAt, out var exp) && exp > DateTimeOffset.UtcNow.AddSeconds(60));
        if (fresh)
        {
            return token;
        }

        var refreshToken = _secrets.Get($"{Key}.refresh_token");
        if (string.IsNullOrEmpty(refreshToken))
        {
            return token; // expired, nothing to refresh with — let the call fail and surface it
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _secrets.Get($"{Key}.client_id") ?? "",
            ["client_secret"] = _secrets.Get($"{Key}.client_secret") ?? "",
        };
        var refreshed = await PostTokenAsync(form, ct);
        StoreToken(refreshed);
        return refreshed.AccessToken;
    }

    // ── polling ────────────────────────────────────────────────────────

    public async Task RefreshAsync(CancellationToken ct)
    {
        if (!IsConnected)
        {
            Latest = null;
            return;
        }

        try
        {
            var oauth = Connection == ConnectionKind.OAuth;
            var bearer = oauth ? await ValidAccessTokenAsync(ct) : null;
            var apiKey = oauth ? null : _secrets.Get($"{Key}.api_key");

            var raw = await FetchAsync(oauth, bearer, apiKey, ct);
            if (raw is null)
            {
                Latest = Latest is null ? null : Latest with { ActiveNow = false };
                return;
            }

            var totalForDelta = raw.CumulativeTotalSeconds ?? raw.SecondsToday;
            var firstPoll = _lastTotal is null;
            var delta = firstPoll ? 0 : totalForDelta - _lastTotal!.Value;
            _lastTotal = totalForDelta;
            _secrets.Set($"{Key}.last_total", totalForDelta.ToString("R"));

            var active = raw.ReportsActive ?? (!firstPoll && delta > 0.5);

            Latest = new CodingSnapshot(
                DisplayName, raw.Project, raw.Language, raw.Editor,
                raw.SecondsToday, DateTimeOffset.UtcNow, active);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{Source} refresh failed", DisplayName);
            Latest = Latest is null ? null : Latest with { ActiveNow = false };
        }
    }

    /// <summary>Do the provider-specific GET and shape the response.</summary>
    protected abstract Task<RawActivity?> FetchAsync(bool oauth, string? bearer, string? apiKey, CancellationToken ct);

    protected HttpClient Http => _http;

    protected sealed record RawActivity(
        string? Project,
        string? Language,
        string? Editor,
        double SecondsToday,
        double? CumulativeTotalSeconds,
        bool? ReportsActive);

    protected static string Truncate(string s) => s.Length <= 300 ? s : s[..300];

    protected static string? TopBySeconds(JsonElement parent, string arrayName)
    {
        if (!parent.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        double bestSeconds = -1;
        foreach (var item in array.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var seconds = item.TryGetProperty("total_seconds", out var s) && s.TryGetDouble(out var d) ? d : 0;
            if (!string.IsNullOrWhiteSpace(name) && seconds > bestSeconds &&
                !string.Equals(name, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                best = name;
                bestSeconds = seconds;
            }
        }
        return best;
    }

    private sealed record TokenResponse(string AccessToken, string? RefreshToken, int? ExpiresInSeconds)
    {
        public static TokenResponse Parse(string body)
        {
            body = body.Trim();
            if (body.StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
                if (string.IsNullOrEmpty(access))
                {
                    throw new InvalidOperationException($"Token response had no access_token: {Truncate(body)}");
                }
                var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
                int? expires = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var ei) ? ei : null;
                return new TokenResponse(access, refresh, expires);
            }

            // WakaTime historically answers x-www-form-urlencoded.
            var pairs = ParseFormEncoded(body);
            var accessToken = pairs.GetValueOrDefault("access_token")
                              ?? throw new InvalidOperationException($"Token response had no access_token: {Truncate(body)}");
            int? exp = int.TryParse(pairs.GetValueOrDefault("expires_in"), out var x) ? x : null;
            return new TokenResponse(accessToken, pairs.GetValueOrDefault("refresh_token"), exp);
        }

        private static Dictionary<string, string> ParseFormEncoded(string body)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                var k = Uri.UnescapeDataString(pair[..eq]);
                var v = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                result[k] = v;
            }
            return result;
        }
    }
}
