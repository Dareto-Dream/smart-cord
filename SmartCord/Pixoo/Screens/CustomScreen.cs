using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SmartCord.Pixoo.Screens;

/// <summary>
/// On-disk shape of a user-authored Pixoo screen. One JSON file, one screen — see
/// <c>STANDARDS.md</c> Part 3 for the full field reference and token list.
/// </summary>
public sealed class CustomScreenSpec
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Background { get; set; } = "#000000";

    /// <summary>"always" (default), "gpu", "compute", "nowplaying", or "coding".</summary>
    public string AvailableWhen { get; set; } = "always";

    public List<CustomElementSpec> Elements { get; set; } = [];
}

public sealed class CustomElementSpec
{
    /// <summary>"text", "bar", or "rect".</summary>
    public string Type { get; set; } = "text";

    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public int Scale { get; set; } = 1;
    public bool Fill { get; set; } = true;
    public bool Center { get; set; }
    public string Color { get; set; } = "#ffffff";
    public string Track { get; set; } = "#1c1e24";

    /// <summary>"text" elements: literal text, may contain {token} placeholders.</summary>
    public string? Text { get; set; }

    /// <summary>"bar" elements: a 0..1 literal or a {token} that resolves to one.</summary>
    public string? Value { get; set; }
}

/// <summary>
/// Renders a <see cref="CustomScreenSpec"/> loaded from disk — the "open standard"
/// half of the Pixoo screen system: no recompiling SmartCord to add a screen, just
/// drop a JSON file describing text/bar/rect elements bound to a small set of
/// documented data tokens into <see cref="CustomScreenLoader.Directory"/>.
/// </summary>
public sealed class CustomScreen(CustomScreenSpec spec) : IPixooScreen
{
    private static readonly Regex TokenPattern = new(@"\{([\w.]+)\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, Func<PixooContext, string>> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["clock.hhmm"] = ctx => ctx.Now.ToString("HH:mm"),
        ["clock.hhmmss"] = ctx => ctx.Now.ToString("HH:mm:ss"),
        ["system.cpuPct"] = ctx => ((int)Math.Round(ctx.System.CpuPct)).ToString(),
        ["system.cpuFraction"] = ctx => (ctx.System.CpuPct / 100.0).ToString("0.###", CultureInfo.InvariantCulture),
        ["system.ramUsedMb"] = ctx => ctx.System.RamUsedMb.ToString(),
        ["system.ramTotalMb"] = ctx => ctx.System.RamTotalMb.ToString(),
        ["system.ramFraction"] = ctx => ctx.System.RamFraction.ToString("0.###", CultureInfo.InvariantCulture),
        ["gpu.utilPct"] = ctx => (ctx.Gpu?.UtilPct ?? 0).ToString(),
        ["gpu.utilFraction"] = ctx => ((ctx.Gpu?.UtilPct ?? 0) / 100.0).ToString("0.###", CultureInfo.InvariantCulture),
        ["gpu.tempC"] = ctx => (ctx.Gpu?.TempC ?? 0).ToString(),
        ["gpu.name"] = ctx => ctx.Gpu?.ShortName ?? "",
        ["wakatime.project"] = ctx => ctx.Controller.DrivingCodingActivity?.Project ?? "",
        ["wakatime.language"] = ctx => ctx.Controller.DrivingCodingActivity?.Language ?? "",
        ["wakatime.today"] = ctx => ctx.Controller.DrivingCodingActivity is { } s
            ? Integrations.CodingSnapshot.HumanizeDuration(s.SecondsToday)
            : "",
        ["nowplaying.title"] = ctx => ctx.NowPlaying?.Title ?? "",
        ["nowplaying.artist"] = ctx => ctx.NowPlaying?.Artist ?? "",
        ["nowplaying.progress"] = ctx => (ctx.NowPlaying?.Progress ?? 0).ToString("0.###", CultureInfo.InvariantCulture),
        ["discord.status"] = ctx => ctx.Controller.LastStatus,
        ["project.title"] = ctx => ctx.Controller.ActiveProject?.Title ?? "",
    };

    public string Id => spec.Id;
    public string Label => string.IsNullOrWhiteSpace(spec.Label) ? spec.Id : spec.Label;

    public bool IsAvailable(PixooContext ctx) => spec.AvailableWhen.ToLowerInvariant() switch
    {
        "gpu" => ctx.Gpu is not null,
        "compute" => ctx.Compute is not null,
        "nowplaying" => ctx.NowPlaying is not null,
        "coding" => ctx.Controller.DrivingCodingActivity is not null,
        _ => true,
    };

    public void Render(PixooCanvas c, PixooContext ctx)
    {
        c.Fill(ParseHex(spec.Background, Color.Black));

        foreach (var el in spec.Elements)
        {
            switch (el.Type.ToLowerInvariant())
            {
                case "text":
                    var text = Resolve(el.Text ?? "", ctx);
                    var scale = Math.Max(1, el.Scale);
                    if (el.Center)
                    {
                        c.DrawTextCenter(el.Y, text, ParseHex(el.Color, Color.White), scale);
                    }
                    else
                    {
                        c.DrawText(el.X, el.Y, text, ParseHex(el.Color, Color.White), scale);
                    }
                    break;

                case "bar":
                    var fraction = ResolveFraction(el.Value ?? "0", ctx);
                    c.ProgressBar(el.X, el.Y, el.W, el.H, fraction, ParseHex(el.Color, Color.White), ParseHex(el.Track, Color.FromArgb(28, 30, 36)));
                    break;

                case "rect":
                    c.Rect(el.X, el.Y, el.W, el.H, ParseHex(el.Color, Color.White), el.Fill);
                    break;
            }
        }
    }

    private static string Resolve(string template, PixooContext ctx) =>
        TokenPattern.Replace(template, m => Tokens.TryGetValue(m.Groups[1].Value, out var fn) ? fn(ctx) : m.Value);

    private static double ResolveFraction(string valueOrToken, PixooContext ctx)
    {
        var resolved = Resolve(valueOrToken, ctx);
        return double.TryParse(resolved, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? Math.Clamp(d, 0, 1)
            : 0;
    }

    private static Color ParseHex(string hex, Color fallback)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6 &&
            int.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, null, out var r) &&
            int.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, null, out var g) &&
            int.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, null, out var b))
        {
            return Color.FromArgb(r, g, b);
        }
        return fallback;
    }
}

/// <summary>Discovers and parses <c>*.json</c> screen specs from disk.</summary>
public static class CustomScreenLoader
{
    public static string Directory => Path.Combine(AppPaths.RootDirectory, "pixoo-screens");

    public static IReadOnlyList<IPixooScreen> LoadAll(ILogger logger)
    {
        var screens = new List<IPixooScreen>();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create custom Pixoo screens directory");
            return screens;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var spec = JsonSerializer.Deserialize<CustomScreenSpec>(
                    File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (spec is null || string.IsNullOrWhiteSpace(spec.Id))
                {
                    logger.LogWarning("Skipped custom Pixoo screen {File}: missing \"id\"", Path.GetFileName(file));
                    continue;
                }
                if (!seenIds.Add(spec.Id))
                {
                    logger.LogWarning("Skipped custom Pixoo screen {File}: duplicate id \"{Id}\"", Path.GetFileName(file), spec.Id);
                    continue;
                }

                screens.Add(new CustomScreen(spec));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load custom Pixoo screen {File}", Path.GetFileName(file));
            }
        }
        return screens;
    }
}
