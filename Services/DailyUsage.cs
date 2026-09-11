using System;
using System.IO;
using System.Text.Json;

namespace Momo;

public sealed record DailyProgress(double Budget, double Used, double Percent, bool Partial, DateTimeOffset StartedAt)
{
    public double RemainingPercent => Math.Max(0, 100 - Percent);
}

// The quota endpoint has no daily history. Keep observed percentage-point deltas only.
public sealed class DailyUsage
{
    public string Day { get; set; } = "";
    public string Scope { get; set; } = "";
    public DateTimeOffset Reset { get; set; }
    public DateTimeOffset LastSample { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public double LastUsed { get; set; }
    public double UsedToday { get; set; }
    public bool Partial { get; set; } = true;
    private static string LocalDay(DateTimeOffset at, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd");
    // The server's reset epoch can jitter by a second between reads of the same window.
    private static bool SameWindow(DateTimeOffset a, DateTimeOffset b) => Math.Abs((a - b).TotalSeconds) <= 120;
    private static bool Valid(QuotaWindow? week, DateTimeOffset now) => week?.Minutes == 10080 && week.UsedPercent is double used && double.IsFinite(used) && used >= 0 && used <= 100 && week.ResetsAt is {} reset && reset > now && reset - now <= TimeSpan.FromDays(7.01);

    public void Observe(QuotaWindow? week, DateTimeOffset at, TimeZoneInfo zone, string scope)
    {
        if (!Valid(week, at) || at < LastSample) return;
        double used = week!.UsedPercent!.Value;
        string day = LocalDay(at, zone);
        bool cycleChanged = !SameWindow(Reset, week.ResetsAt!.Value) || Scope != scope;
        bool corrected = used < LastUsed - .000001;
        if (day != Day || cycleChanged || corrected)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            bool midnightSample = local.TimeOfDay.TotalMinutes < 2 && at - LastSample <= TimeSpan.FromMinutes(2);
            Partial = cycleChanged || corrected || !midnightSample;
            Day = day; Scope = scope; Reset = week.ResetsAt!.Value;
            UsedToday = 0; StartedAt = at;
        }
        else UsedToday += Math.Max(0, used - LastUsed);
        Reset = week.ResetsAt!.Value; LastUsed = used; LastSample = at;
    }

    public DailyProgress? Calculate(QuotaWindow? week, DateTimeOffset now, TimeZoneInfo zone, string scope, bool fresh = true)
    {
        if (!Valid(week, now) || now < LastSample || Day != LocalDay(now, zone) || Scope != scope || !SameWindow(Reset, week!.ResetsAt!.Value) || LastSample == default) return null;
        double days = Math.Max(1, (Reset - (fresh ? now : LastSample)).TotalDays);
        double budget = (100 - LastUsed) / days;
        double percent = budget <= 0 ? 100 : UsedToday / budget * 100;
        return new DailyProgress(budget, UsedToday, percent, Partial, StartedAt);
    }

    private static string FilePath => Path.Combine(App.DataDirectory, "daily-usage.json");
    public static DailyUsage Load()
    {
        try
        {
            var state = JsonSerializer.Deserialize<DailyUsage>(File.ReadAllText(FilePath));
            if (state is not null && state.LastSample <= DateTimeOffset.UtcNow.AddMinutes(5) && double.IsFinite(state.LastUsed) && state.LastUsed is >= 0 and <= 100 && double.IsFinite(state.UsedToday) && state.UsedToday is >= 0 and <= 100) return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return new();
    }
    public void Save()
    {
        if (App.IsTestMode) return;
        try { Directory.CreateDirectory(App.DataDirectory); File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this)); File.Move(FilePath + ".tmp", FilePath, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
