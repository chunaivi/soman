using System.Globalization;
using Quartz;

namespace SoMan.Services.Scheduler;

/// <summary>
/// Enumerates the "easy" schedule presets surfaced in the Scheduler dialog.
/// Each value maps to a Quartz-style cron expression via <see cref="CronHelper"/>.
/// </summary>
public enum SchedulePreset
{
    Every5Minutes,
    Every15Minutes,
    Every30Minutes,
    Hourly,
    Every6Hours,
    DailyAtTime,
    WeekdaysAtTime,
    WeeklyAtTime,
    Custom,
}

/// <summary>
/// Day-of-week flags for the "Time of day" mode. Matches Quartz's cron DOW
/// abbreviations (SUN..SAT).
/// </summary>
[Flags]
public enum DayOfWeekFlags
{
    None      = 0,
    Sunday    = 1 << 0,
    Monday    = 1 << 1,
    Tuesday   = 1 << 2,
    Wednesday = 1 << 3,
    Thursday  = 1 << 4,
    Friday    = 1 << 5,
    Saturday  = 1 << 6,
    Weekdays  = Monday | Tuesday | Wednesday | Thursday | Friday,
    AllDays   = Weekdays | Saturday | Sunday,
}

/// <summary>
/// Builds and inspects Quartz cron expressions used by <see cref="SchedulerService"/>.
/// Quartz uses 6- or 7-field cron (sec min hour dom month dow [year]); all cron
/// expressions built here use the 7-field form with '?' for whichever of dom/dow
/// is not specified — Quartz rejects expressions that specify both.
/// </summary>
public static class CronHelper
{
    /// <summary>
    /// Build a cron expression from the UI inputs. <paramref name="timeOfDay"/>
    /// is interpreted as local time. <paramref name="days"/> is only consulted for
    /// <see cref="SchedulePreset.WeekdaysAtTime"/> / <see cref="SchedulePreset.WeeklyAtTime"/>
    /// / <see cref="SchedulePreset.DailyAtTime"/>. <paramref name="customCron"/>
    /// is returned verbatim for <see cref="SchedulePreset.Custom"/>.
    /// </summary>
    public static string BuildCron(
        SchedulePreset preset,
        TimeSpan timeOfDay,
        DayOfWeekFlags days,
        string? customCron = null)
    {
        int h = timeOfDay.Hours;
        int m = timeOfDay.Minutes;

        return preset switch
        {
            SchedulePreset.Every5Minutes   => "0 0/5 * * * ?",
            SchedulePreset.Every15Minutes  => "0 0/15 * * * ?",
            SchedulePreset.Every30Minutes  => "0 0/30 * * * ?",
            SchedulePreset.Hourly          => "0 0 * * * ?",
            SchedulePreset.Every6Hours     => "0 0 0/6 * * ?",
            SchedulePreset.DailyAtTime     => $"0 {m} {h} * * ?",
            SchedulePreset.WeekdaysAtTime  => $"0 {m} {h} ? * MON-FRI",
            SchedulePreset.WeeklyAtTime    => $"0 {m} {h} ? * {DaysToCron(days == DayOfWeekFlags.None ? DayOfWeekFlags.Monday : days)}",
            SchedulePreset.Custom          => string.IsNullOrWhiteSpace(customCron) ? "0 0 * * * ?" : customCron!,
            _                              => "0 0 * * * ?",
        };
    }

    /// <summary>
    /// Validates a cron expression using Quartz's own parser.
    /// </summary>
    public static bool TryValidate(string cron, out string? error)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            error = "Cron expression is empty.";
            return false;
        }

        try
        {
            CronExpression.ValidateExpression(cron);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Returns the next fire time for a cron expression in local time, or null
    /// if the expression is invalid or never fires.
    /// </summary>
    public static DateTime? GetNextFireTime(string cron, DateTime? after = null)
    {
        if (!TryValidate(cron, out _)) return null;
        try
        {
            var expr = new CronExpression(cron);
            var baseTime = (after ?? DateTime.UtcNow).ToUniversalTime();
            var next = expr.GetNextValidTimeAfter(baseTime);
            return next?.LocalDateTime;
        }
        catch { return null; }
    }

    /// <summary>
    /// Short human-readable description for a cron — e.g. "Daily at 09:00" or
    /// the raw expression if we can't recognise it.
    /// </summary>
    public static string Describe(string cron, SchedulePreset preset, TimeSpan time, DayOfWeekFlags days)
    {
        string t = $"{time.Hours:D2}:{time.Minutes:D2}";
        return preset switch
        {
            SchedulePreset.Every5Minutes   => "Every 5 minutes",
            SchedulePreset.Every15Minutes  => "Every 15 minutes",
            SchedulePreset.Every30Minutes  => "Every 30 minutes",
            SchedulePreset.Hourly          => "Every hour",
            SchedulePreset.Every6Hours     => "Every 6 hours",
            SchedulePreset.DailyAtTime     => $"Daily at {t}",
            SchedulePreset.WeekdaysAtTime  => $"Weekdays at {t}",
            SchedulePreset.WeeklyAtTime    => $"{DaysToHumanReadable(days)} at {t}",
            SchedulePreset.Custom          => $"Custom: {cron}",
            _                              => cron,
        };
    }

    private static string DaysToCron(DayOfWeekFlags days)
    {
        var parts = new List<string>();
        if (days.HasFlag(DayOfWeekFlags.Sunday))    parts.Add("SUN");
        if (days.HasFlag(DayOfWeekFlags.Monday))    parts.Add("MON");
        if (days.HasFlag(DayOfWeekFlags.Tuesday))   parts.Add("TUE");
        if (days.HasFlag(DayOfWeekFlags.Wednesday)) parts.Add("WED");
        if (days.HasFlag(DayOfWeekFlags.Thursday))  parts.Add("THU");
        if (days.HasFlag(DayOfWeekFlags.Friday))    parts.Add("FRI");
        if (days.HasFlag(DayOfWeekFlags.Saturday))  parts.Add("SAT");
        return string.Join(",", parts);
    }

    private static string DaysToHumanReadable(DayOfWeekFlags days)
    {
        if (days == DayOfWeekFlags.AllDays) return "Every day";
        if (days == DayOfWeekFlags.Weekdays) return "Weekdays";

        var parts = new List<string>();
        if (days.HasFlag(DayOfWeekFlags.Monday))    parts.Add("Mon");
        if (days.HasFlag(DayOfWeekFlags.Tuesday))   parts.Add("Tue");
        if (days.HasFlag(DayOfWeekFlags.Wednesday)) parts.Add("Wed");
        if (days.HasFlag(DayOfWeekFlags.Thursday))  parts.Add("Thu");
        if (days.HasFlag(DayOfWeekFlags.Friday))    parts.Add("Fri");
        if (days.HasFlag(DayOfWeekFlags.Saturday))  parts.Add("Sat");
        if (days.HasFlag(DayOfWeekFlags.Sunday))    parts.Add("Sun");
        return parts.Count == 0 ? "(no day selected)" : string.Join("/", parts);
    }
}
