using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SmartCord;

/// <summary>One day's coding seconds, broken down by project.</summary>
public sealed class DayTotals
{
    public Dictionary<string, double> SecondsByProject { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Rolled-up totals for the weekly recap toast.</summary>
public sealed record WeeklyRecap(double TotalSeconds, string? TopProject, double TopProjectSeconds, int ProjectCount)
{
    public bool HasData => TotalSeconds > 0;
}

/// <summary>
/// A small local log of daily coding seconds per project, fed from whatever
/// <see cref="CodingSnapshot"/> is currently driving presence. Not a real time
/// tracker — WakaTime/Hackatime already own that — this just remembers enough of
/// what we already saw to summarize "your week" in a toast without a backend.
/// Persisted to <c>%AppData%\SmartCord\activity-log.json</c>, trimmed to the
/// trailing 14 days on every write so it never grows unbounded.
/// </summary>
public sealed class ActivityLog
{
    private const int RetainDays = 14;
    private static readonly string Path_ = Path.Combine(AppPaths.RootDirectory, "activity-log.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly ILogger<ActivityLog> _logger;
    private readonly object _gate = new();
    private Dictionary<string, DayTotals> _byDate;

    public ActivityLog(ILogger<ActivityLog> logger)
    {
        _logger = logger;
        _byDate = Load();
    }

    /// <summary>
    /// Records today's running total for a project. WakaTime/Hackatime's
    /// "seconds today" only grows within a day, so we keep the max seen rather
    /// than summing samples (which would double-count on every poll).
    /// </summary>
    public void Record(string? project, double secondsToday)
    {
        if (secondsToday <= 0)
        {
            return;
        }

        var proj = string.IsNullOrWhiteSpace(project) ? "Other" : project;
        var date = DateTime.Now.ToString("yyyy-MM-dd");

        lock (_gate)
        {
            if (!_byDate.TryGetValue(date, out var day))
            {
                day = new DayTotals();
                _byDate[date] = day;
            }

            var previous = day.SecondsByProject.GetValueOrDefault(proj);
            if (secondsToday <= previous)
            {
                return; // nothing new to persist
            }

            day.SecondsByProject[proj] = secondsToday;
            Prune();
            Save();
        }
    }

    /// <summary>Sums the trailing 7 calendar days (inclusive of <paramref name="asOf"/>) per project.</summary>
    public WeeklyRecap BuildRecap(DateTime asOf)
    {
        lock (_gate)
        {
            var cutoff = asOf.Date.AddDays(-6);
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var (dateText, day) in _byDate)
            {
                if (!DateTime.TryParse(dateText, out var date) || date.Date < cutoff || date.Date > asOf.Date)
                {
                    continue;
                }
                foreach (var (project, seconds) in day.SecondsByProject)
                {
                    totals[project] = totals.GetValueOrDefault(project) + seconds;
                }
            }

            var top = totals.OrderByDescending(kv => kv.Value).FirstOrDefault();
            return new WeeklyRecap(totals.Values.Sum(), top.Key, top.Value, totals.Count);
        }
    }

    private void Prune()
    {
        var cutoff = DateTime.Now.Date.AddDays(-RetainDays);
        foreach (var key in _byDate.Keys.ToList())
        {
            if (DateTime.TryParse(key, out var date) && date.Date < cutoff)
            {
                _byDate.Remove(key);
            }
        }
    }

    private Dictionary<string, DayTotals> Load()
    {
        try
        {
            if (File.Exists(Path_))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, DayTotals>>(File.ReadAllText(Path_));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read activity log; starting fresh");
        }
        return new Dictionary<string, DayTotals>();
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureCreated();
            var json = JsonSerializer.Serialize(_byDate, Options);
            var temp = Path_ + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, Path_, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist activity log");
        }
    }
}
