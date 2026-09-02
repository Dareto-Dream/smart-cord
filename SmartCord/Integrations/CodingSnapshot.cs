namespace SmartCord.Integrations;

/// <summary>One poll's worth of "what am I coding" from a time-tracking source.</summary>
public sealed record CodingSnapshot(
    string Source,
    string? Project,
    string? Language,
    string? Editor,
    double SecondsToday,
    DateTimeOffset CapturedAtUtc,
    bool ActiveNow)
{
    public string TodayText => HumanizeDuration(SecondsToday);

    /// <summary>e.g. "Glyphs · 4h 32m today" — the second line of the presence card.</summary>
    public string StateLine
    {
        get
        {
            var project = string.IsNullOrWhiteSpace(Project) ? null : Project;
            var hours = SecondsToday >= 60 ? $"{TodayText} today" : null;
            return (project, hours) switch
            {
                (not null, not null) => $"{project} · {hours}",
                (not null, null) => project!,
                (null, not null) => hours!,
                _ => "Coding",
            };
        }
    }

    public string DetailsLine =>
        string.IsNullOrWhiteSpace(Language) ? "Coding" : $"Coding in {Language}";

    public static string HumanizeDuration(double seconds)
    {
        if (seconds < 60)
        {
            return $"{(int)seconds}s";
        }
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes}m"
            : $"{span.Minutes}m";
    }
}
