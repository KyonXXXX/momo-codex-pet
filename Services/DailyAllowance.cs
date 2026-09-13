using System;

namespace Momo;

public sealed record DailyAllowance(double WeeklyRemaining, int DaysRemaining, double AvailableWeeklyPercent)
{
    public const double BaseDaily = 100d / 7;
    public double Percent => AvailableWeeklyPercent / BaseDaily * 100;
    // Debt beyond today's allocation includes any overuse carried from earlier quota days.
    public double OverdrawnWeeklyPercent
    {
        get
        {
            double debt = (DaysRemaining - 1) * BaseDaily - WeeklyRemaining;
            return debt > 1e-10 ? debt : 0;
        }
    }
    public double OverdrawnPercent => OverdrawnWeeklyPercent / BaseDaily * 100;
    public bool IsOverdrawn => OverdrawnWeeklyPercent > 0;

    // Stateless: identical account snapshots yield identical allowances on every computer.
    // A day is a 24-hour slice of the account's weekly reset cycle, not local midnight.
    public static DailyAllowance? Calculate(QuotaWindow? week, DateTimeOffset at)
    {
        if (week?.Minutes != 10080 || week.UsedPercent is not double used ||
            !double.IsFinite(used) || used < 0 || used > 100 ||
            week.ResetsAt is not DateTimeOffset reset || reset <= at ||
            reset - at > TimeSpan.FromDays(7.01)) return null;

        int days = Math.Clamp((int)Math.Ceiling((reset - at).TotalDays), 1, 7);
        double remaining = 100 - used;
        double available = remaining >= days * BaseDaily
            ? remaining / days
            : Math.Max(0, remaining - (days - 1) * BaseDaily);
        return new(remaining, days, Math.Clamp(available, 0, remaining));
    }

    public static DailyAllowance? ForDisplay(QuotaWindow? week, DateTimeOffset fetchedAt, DateTimeOffset now, bool fresh)
    {
        if (fetchedAt > now) return null;
        var current = Calculate(week, now);
        if (fresh) return current;
        var previous = Calculate(week, fetchedAt);
        // An offline snapshot cannot grant a new day's allowance or survive a weekly reset.
        return current?.DaysRemaining == previous?.DaysRemaining ? previous : null;
    }
}
