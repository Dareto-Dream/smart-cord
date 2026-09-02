using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.Pixoo;

/// <summary>
/// Minimal client for the Divoom Pixoo64 local HTTP API, ported from
/// adamkdean/pixoo-api (GPL-3.0): everything is a JSON POST to
/// <c>http://&lt;host&gt;/post</c>. We only need the framebuffer push, the GIF-id
/// reset, and brightness.
/// </summary>
public sealed class PixooClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly ILogger _logger;
    private readonly object _gate = new();

    private string _host;
    private int _picId = 1;

    public PixooClient(string host, ILogger logger)
    {
        _host = host;
        _logger = logger;
    }

    public string Host
    {
        get => _host;
        set => _host = value;
    }

    private string Endpoint => $"http://{_host}/post";

    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            await PostAsync(new { Command = "Device/GetDeviceTime" }, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task ResetGifIdAsync(CancellationToken ct)
    {
        await PostAsync(new { Command = "Draw/ResetHttpGifId" }, ct);
        lock (_gate)
        {
            _picId = 1;
        }
    }

    public async Task SetBrightnessAsync(int brightness, CancellationToken ct)
    {
        await PostAsync(new { Command = "Channel/SetBrightness", Brightness = Math.Clamp(brightness, 0, 100) }, ct);
    }

    /// <summary>Pushes one static 64×64 frame (base64 RGB, row-major).</summary>
    public async Task PushFrameAsync(string picDataBase64, CancellationToken ct)
    {
        int id;
        bool needsReset;
        lock (_gate)
        {
            needsReset = _picId >= 900;
        }
        if (needsReset)
        {
            await ResetGifIdAsync(ct);
        }

        lock (_gate)
        {
            id = _picId++;
        }

        await PostAsync(new
        {
            Command = "Draw/SendHttpGif",
            PicNum = 1,
            PicWidth = PixooCanvas.Size,
            PicOffset = 0,
            PicID = id,
            PicSpeed = 1000,
            PicData = picDataBase64,
        }, ct);
    }

    // The Pixoo API is case-sensitive ("Command", "PicData", "PicID", …), so never
    // let a camelCase naming policy near it — serialize property names verbatim.
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    private async Task PostAsync(object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), Json);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(Endpoint, content, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (TryReadErrorCode(body, out var code) && code != 0)
        {
            throw new PixooException($"Device returned error_code {code}");
        }
    }

    private static bool TryReadErrorCode(string body, out int code)
    {
        code = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("error_code", out var ec))
            {
                return false;
            }

            code = ec.ValueKind switch
            {
                JsonValueKind.Number when ec.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(ec.GetString(), out var s) => s,
                _ => 0,
            };
            return true;
        }
        catch (JsonException)
        {
            // device sometimes replies with a bare "OK"
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class PixooException(string message) : Exception(message);
