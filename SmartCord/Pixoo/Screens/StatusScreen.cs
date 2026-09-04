using SmartCord.Integrations;

namespace SmartCord.Pixoo.Screens;

/// <summary>The original SmartCord dashboard — coding activity / preset / idle + Discord + clock.</summary>
public sealed class StatusScreen : IPixooScreen
{
    public string Id => "status";
    public string Label => "SmartCord status";

    public bool IsAvailable(PixooContext ctx) => true;

    public void Render(PixooCanvas canvas, PixooContext ctx) =>
        PixooDashboard.Render(canvas, BuildModel(ctx));

    public static PixooDashboardModel BuildModel(PixooContext ctx)
    {
        var c = ctx.Controller;
        var accentIdle = Color.FromArgb(88, 101, 242);
        var accentGrey = Color.FromArgb(90, 93, 102);
        var green = Color.FromArgb(35, 165, 90);
        var red = Color.FromArgb(210, 60, 60);
        var yellow = Color.FromArgb(230, 170, 40);

        var configured = c.IsConfigured;
        var connected = c.IsConnected;
        var statusDot = !configured ? red : connected ? green : yellow;
        var discordText = !configured
            ? "no client id"
            : connected ? DiscordUser(c.LastStatus)
            : c.IsEnabled ? "connecting" : "presence off";

        if (c.SessionLocked && c.Settings.Presence.ClearOnLock)
        {
            return new PixooDashboardModel
            {
                Headline = "LOCKED", Accent = accentGrey, Detail = "session locked",
                DiscordConnected = connected, DiscordText = discordText, StatusDot = yellow, Clock = ctx.Now,
            };
        }

        if (!c.IsEnabled)
        {
            return new PixooDashboardModel
            {
                Headline = "PAUSED", Accent = accentGrey, Detail = "rich presence off",
                DiscordConnected = connected, DiscordText = discordText, StatusDot = accentGrey, Clock = ctx.Now,
            };
        }

        var coding = c.DrivingCodingActivity;
        if (coding is not null)
        {
            var target = Math.Max(0.5, ctx.Settings.Pixoo.DailyTargetHours);
            return new PixooDashboardModel
            {
                Coding = true,
                Headline = coding.Source,
                Accent = green,
                Language = coding.Language,
                Project = coding.Project ?? c.ActiveProject?.Title,
                Detail = coding.SecondsToday >= 60 ? $"{CodingSnapshot.HumanizeDuration(coding.SecondsToday)} today" : "just started",
                ShowProgress = coding.SecondsToday >= 60,
                ProgressFraction = coding.SecondsToday / 3600.0 / target,
                DiscordConnected = connected, DiscordText = discordText, StatusDot = statusDot, Clock = ctx.Now,
            };
        }

        if (c.Mode == PresenceMode.Custom)
        {
            var custom = c.CustomPresence;
            return new PixooDashboardModel
            {
                Headline = "CUSTOM", Accent = accentIdle, Detail = custom.Details, Project = custom.State,
                DiscordConnected = connected, DiscordText = discordText, StatusDot = statusDot, Clock = ctx.Now,
            };
        }

        var preset = c.ManualPreset ?? c.ResolveDetectedPreset();
        var idle = preset.Key is "idle" or "away";
        return new PixooDashboardModel
        {
            Headline = preset.Label,
            Accent = idle ? accentGrey : accentIdle,
            Detail = preset.Details,
            Project = c.ActiveProject?.Title,
            DiscordConnected = connected, DiscordText = discordText, StatusDot = statusDot, Clock = ctx.Now,
        };
    }

    private static string DiscordUser(string lastStatus)
    {
        const string marker = "Connected as ";
        var i = lastStatus.IndexOf(marker, StringComparison.Ordinal);
        return i >= 0 ? lastStatus[(i + marker.Length)..].Trim() : "connected";
    }
}
