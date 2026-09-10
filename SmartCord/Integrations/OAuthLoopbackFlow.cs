using System.Net;
using System.Diagnostics;

namespace SmartCord.Integrations;

/// <summary>
/// The desktop OAuth redirect dance, same shape as mantle's Hackatime/Spotify
/// connectors: spin up a one-shot loopback HTTP server, open the system browser
/// at the authorize URL, and wait for the provider to redirect back with
/// <c>?code=…</c>. Returns the authorization code (or throws on error/timeout).
/// </summary>
public static class OAuthLoopbackFlow
{
    public static async Task<string> AuthorizeAsync(
        string authorizeUrl,
        int port,
        string callbackPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var prefix = $"http://127.0.0.1:{port}{callbackPath}";
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Couldn't open the loopback port {port} for the OAuth redirect. " +
                "Another app may be using it, or the URL ACL is missing.", ex);
        }

        OpenBrowser(authorizeUrl);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            while (true)
            {
                var contextTask = listener.GetContextAsync();
                var completed = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeoutCts.Token));
                if (completed != contextTask)
                {
                    throw new TimeoutException("Timed out waiting for the OAuth redirect.");
                }

                var context = await contextTask;
                var query = context.Request.QueryString;
                var code = query["code"];
                var error = query["error"];

                if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(error))
                {
                    Respond(context, string.IsNullOrEmpty(error));
                    if (!string.IsNullOrEmpty(error))
                    {
                        throw new InvalidOperationException($"Authorization was denied ({error}).");
                    }
                    return code!;
                }

                // Ignore favicon and stray hits; keep listening.
                Respond(context, false, "Waiting for the authorization redirect…");
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void Respond(HttpListenerContext context, bool ok, string? message = null)
    {
        var body = message ?? (ok
            ? "SmartCord is connected. You can close this tab."
            : "SmartCord authorization failed. You can close this tab.");
        var html = $"<!doctype html><meta charset=utf-8><title>SmartCord</title>" +
                   $"<body style='font:15px system-ui;background:#1e1f22;color:#f2f3f5;padding:3rem'>{body}</body>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        try
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // client hung up — nothing we can do
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // If the shell won't launch, the caller surfaces the failure; the user
            // can still copy the URL from the logs.
        }
    }
}
