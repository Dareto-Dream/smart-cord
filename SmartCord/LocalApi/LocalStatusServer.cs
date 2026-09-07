using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord.LocalApi;

/// <summary>
/// A tiny loopback-only HTTP server exposing the current presence as JSON
/// (<c>/status</c>) and a ready-to-use browser-source overlay (<c>/</c>) for OBS —
/// the same "read a local HTTP endpoint" pattern SmartCord already uses to read
/// Spectralis's own OBS overlay, just running the other way. Bound to
/// <c>127.0.0.1</c> only; there is no reason for this to ever leave the machine.
/// </summary>
public sealed class LocalStatusServer : IDisposable
{
    private readonly SmartCordController _controller;
    private readonly ILogger<LocalStatusServer> _logger;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private SmartCordSettings _settings;
    private bool _disposed;

    public LocalStatusServer(SmartCordController controller, SmartCordSettings settings, ILogger<LocalStatusServer> logger)
    {
        _controller = controller;
        _settings = settings;
        _logger = logger;
    }

    public bool IsRunning => _listener is { IsListening: true };
    public string? LastError { get; private set; }
    public int Port => _settings.LocalApi.Port;
    public string OverlayUrl => $"http://127.0.0.1:{Port}/";
    public string StatusUrl => $"http://127.0.0.1:{Port}/status";

    public void Start()
    {
        if (_disposed || !_settings.LocalApi.Enabled || IsRunning)
        {
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            _listener.Start();
            LastError = null;
            _ = Task.Run(() => AcceptLoopAsync(_listener, _cts.Token));
            _logger.LogInformation("Local status server listening on {Port}", Port);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "Local status server failed to start on port {Port}", Port);
            _listener = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // best effort
        }
        _listener = null;
    }

    public void UpdateSettings(SmartCordSettings settings)
    {
        var wasEnabled = _settings.LocalApi.Enabled;
        var portChanged = _settings.LocalApi.Port != settings.LocalApi.Port;
        _settings = settings;

        if (settings.LocalApi.Enabled && (!wasEnabled || portChanged))
        {
            Stop();
            Start();
        }
        else if (!settings.LocalApi.Enabled && wasEnabled)
        {
            Stop();
        }
    }

    private async Task AcceptLoopAsync(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                return; // listener stopped
            }

            _ = Task.Run(() => HandleAsync(context), ct);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (string.Equals(path, "/status", StringComparison.OrdinalIgnoreCase))
            {
                await WriteAsync(context, "application/json", BuildStatusJson());
            }
            else
            {
                await WriteAsync(context, "text/html; charset=utf-8", OverlayHtml);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local status server request failed");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch
            {
                // response already gone
            }
        }
    }

    private string BuildStatusJson()
    {
        var payload = _controller.CurrentPayload;
        return JsonSerializer.Serialize(new
        {
            enabled = _controller.IsEnabled,
            connected = _controller.IsConnected,
            streaming = _controller.IsStreaming,
            label = payload?.Label ?? "",
            details = payload?.Details ?? "",
            state = payload?.State ?? "",
            project = _controller.ActiveProject?.Title,
            startedAtUtc = _controller.CurrentStartedAtUtc,
        });
    }

    private static async Task WriteAsync(HttpListenerContext context, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = contentType;
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        context.Response.Headers.Add("Cache-Control", "no-store");
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private const string OverlayHtml = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
          html, body { margin: 0; background: transparent; }
          body { font-family: 'Segoe UI', sans-serif; color: #fff; }
          .card {
            display: inline-flex; flex-direction: column; gap: 3px;
            padding: 10px 16px; border-radius: 10px;
            background: rgba(16, 16, 20, 0.72);
          }
          .details { font-size: 20px; font-weight: 600; line-height: 1.2; }
          .state { font-size: 14px; opacity: 0.75; }
        </style>
        </head>
        <body>
          <div class="card" id="card" hidden>
            <div class="details" id="details"></div>
            <div class="state" id="state"></div>
          </div>
          <script>
            async function tick() {
              try {
                const r = await fetch('/status', { cache: 'no-store' });
                const s = await r.json();
                const card = document.getElementById('card');
                card.hidden = !s.enabled;
                document.getElementById('details').textContent = s.details || 'Idle';
                document.getElementById('state').textContent = s.state || '';
              } catch (e) {
                // server not reachable yet (app still starting) — try again next tick
              }
            }
            tick();
            setInterval(tick, 3000);
          </script>
        </body>
        </html>
        """;

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
