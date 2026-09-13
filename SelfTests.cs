using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Momo;

internal static partial class SelfTests
{
    public static async Task<int> RunAsync(bool live)
    {
        var report = new List<string>(); int failures = 0;
        void Check(string name, Action test) { try { test(); report.Add("PASS " + name); } catch (Exception ex) { failures++; report.Add("FAIL " + name + ": " + ex.Message); } }
        static void Equal<T>(T actual, T expected) { if (!Equals(actual, expected)) throw new Exception($"Expected {expected}, got {actual}"); }
        static UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, DateTimeOffset.UtcNow); }
        RunAutoCareTests(Check);
        RunCodexFollowerTests(Check);
        static void Near(double actual, double expected) { if (Math.Abs(actual - expected) > 1e-8) throw new Exception($"Expected {expected}, got {actual}"); }
        var at = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        DailyAllowance Allow(double remaining, double days) => DailyAllowance.Calculate(new QuotaWindow(100 - remaining, 10080, at.AddDays(days)), at)!;
        Check("Fresh weekly quota starts with one seventh available today", () =>
        { Near(Allow(100, 7).Percent, 100); Near(Allow(90, 7).Percent, 30); });
        Check("Prior overspending reduces today's allowance before future days", () =>
        { Near(Allow(30, 3).AvailableWeeklyPercent, 100d / 70); Near(Allow(30, 3).Percent, 10); });
        Check("Unused quota is redistributed across all remaining days", () =>
        { Near(Allow(60, 3).AvailableWeeklyPercent, 20); Near(Allow(60, 3).Percent, 140); });
        Check("Debt carries into later days without negative allowances", () =>
        { Near(Allow(10, 3).Percent, 0); Near(Allow(10, 2).Percent, 0); Near(Allow(10, 1).Percent, 70); });
        Check("Cross-computer usage and restart need no local baseline", () =>
        {
            Near(Allow(60, 3).Percent, 140);
            Near(Allow(30, 3).Percent, 10);
            Near(Allow(30, 3).Percent, 10);
            Near(Allow(60, 3).Percent, 140);
        });
        Check("Overdraw shows the amount beyond today's allocation in base daily units", () =>
        { Near(Allow(25, 3).OverdrawnPercent, 25); Near(Allow(10, 3).OverdrawnPercent, 130); Equal(Allow(10, 3).IsOverdrawn, true); });
        Check("Exactly exhausted allocation is zero remaining, not overdraw", () =>
        {
            for (int days = 1; days <= 7; days++)
            { var value = Allow((days - 1) * DailyAllowance.BaseDaily, days); Near(value.Percent, 0); Equal(value.IsOverdrawn, false); }
        });
        Check("Carried debt decreases with each new quota day and clears on replenishment", () =>
        { Near(Allow(10, 3).OverdrawnPercent, 130); Near(Allow(10, 2).OverdrawnPercent, 30); Near(Allow(10, 1).OverdrawnPercent, 0); Equal(Allow(100, 7).IsOverdrawn, false); });
        Check("Overdraw responds to global snapshots and preserves small excess", () =>
        {
            Equal(Allow(30, 3).IsOverdrawn, false); Equal(Allow(25, 3).IsOverdrawn, true);
            Near(Allow(25, 3).OverdrawnPercent, 25); Equal(Allow(60, 3).IsOverdrawn, false);
            Near(Allow(2 * DailyAllowance.BaseDaily - .04 / 7, 3).OverdrawnPercent, .04);
        });
        Check("Unused balance is redistributed when the next quota day starts", () =>
        { Near(Allow(60, 4).Percent, 105); Near(Allow(60, 3).Percent, 140); });
        Check("Daily boundaries follow reset-cycle 24-hour slices regardless of timezone", () =>
        {
            Equal(Allow(60, 3.001).DaysRemaining, 4); Equal(Allow(60, 3).DaysRemaining, 3);
            Equal(Allow(60, 2.999).DaysRemaining, 3);
            var week = new QuotaWindow(40, 10080, at.AddDays(2.5));
            Equal(DailyAllowance.Calculate(week, at), DailyAllowance.Calculate(week, at.ToOffset(TimeSpan.FromHours(9))));
            Equal(DailyAllowance.Calculate(week, at), DailyAllowance.Calculate(week with { ResetsAt = week.ResetsAt!.Value.AddSeconds(1) }, at));
        });
        Check("Final partial day grants only actual remaining weekly quota", () =>
        { Near(Allow(25, .1).AvailableWeeklyPercent, 25); Near(Allow(25, .1).Percent, 175); Near(Allow(0, .1).Percent, 0); });
        Check("Missing, invalid and expired weekly data stays unknown", () =>
        {
            var week = new QuotaWindow(40, 10080, at.AddDays(3));
            Equal(DailyAllowance.Calculate(null, at), null);
            Equal(DailyAllowance.Calculate(week with { UsedPercent = null }, at), null);
            Equal(DailyAllowance.Calculate(week with { UsedPercent = double.NaN }, at), null);
            Equal(DailyAllowance.Calculate(week with { UsedPercent = -1 }, at), null);
            Equal(DailyAllowance.Calculate(week with { UsedPercent = 101 }, at), null);
            Equal(DailyAllowance.Calculate(week with { ResetsAt = null }, at), null);
            Equal(DailyAllowance.Calculate(week with { ResetsAt = at }, at), null);
            Equal(DailyAllowance.Calculate(week with { ResetsAt = at.AddDays(8) }, at), null);
            Equal(DailyAllowance.Calculate(week with { Minutes = 300 }, at), null);
        });
        Check("Offline data cannot grant a new day's allowance or survive weekly reset", () =>
        {
            var week = new QuotaWindow(40, 10080, at.AddDays(2.5));
            Equal(DailyAllowance.ForDisplay(week, at, at.AddMinutes(3), false), DailyAllowance.Calculate(week, at));
            Equal(DailyAllowance.ForDisplay(week, at, at.AddHours(13), false), null);
            Equal(DailyAllowance.ForDisplay(week, at, at.AddDays(3), false), null);
            Equal(DailyAllowance.ForDisplay(week, at.AddMinutes(1), at, true), null);
        });
        Check("Increasing account usage never increases same-day allowance", () =>
        {
            for (int day = 1; day <= 7; day++)
            {
                double previous = 0;
                for (int remaining = 0; remaining <= 100; remaining++)
                {
                    var value = Allow(remaining, day);
                    Equal(value.AvailableWeeklyPercent >= previous - 1e-8 && value.AvailableWeeklyPercent <= remaining, true);
                    previous = value.AvailableWeeklyPercent;
                }
                Near(Allow(day * DailyAllowance.BaseDaily, day).Percent, 100);
            }
        });
        Check("Old settings gain healthy pet defaults without changing quota visibility", () =>
        {
            var settings=JsonSerializer.Deserialize<PetSettings>("{\"ShowQuotaBubble\":false,\"CompletedFocus\":7}")!;
            Equal(settings.ShowQuotaBubble,false);Equal(settings.CompletedFocus,7);Equal(settings.Life.Coins,300d);Equal(settings.Life.Mood,"happy");
        });
        Check("Pet care preserves currency on rejected feeding and clamps depleted stats", () =>
        {
            var life=new PetLife {Coins=0,Health=20};
            var food=new PetFood("test","Meal","",null,"eat",10,20,20,0,5,2,1);
            Equal(life.Feed(food,out _),false);Equal(life.Coins,0d);Equal(life.Health,20d);Equal(life.Mood,"ill");
            life.Coins=10;Equal(life.Feed(food,out _),true);Equal(life.Coins,0d);Equal(life.Health,25d);
            life.Hunger=-50;life.Thirst=double.NaN;life.Normalize();Equal(life.Hunger,0d);Equal(life.Thirst,85d);
        });
        Check("Activities pay only elapsed fraction and suspended time is capped", () =>
        {
            var life=new PetLife {Coins=0};var activity=new PetActivity("work","Work","WORK/WorkONE",60,8,3.5,2.5,1,.1,1);
            life.Complete(activity,.5);Equal(life.Coins,240d);
            double hunger=life.Hunger;life.Tick(3600,false,null);
            Equal(hunger-life.Hunger<1,true);
        });
        Check("Opaque bounds exclude transparent padding and include final visible pixel", () =>
        {
            var pixels = new byte[10 * 8 * 4];
            pixels[(2 * 10 + 3) * 4 + 3] = 255;
            pixels[(6 * 10 + 7) * 4 + 3] = 1;
            var bitmap = BitmapSource.Create(10, 8, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 40);
            Equal(AnimationPlayer.OpaqueBounds(bitmap), new System.Windows.Rect(.3, .25, .5, .625));
        });
        Check("Bubble defaults on for existing settings and preserves an explicit off choice", () =>
        {
            Equal(JsonSerializer.Deserialize<PetSettings>("{\"Scale\":1}")!.ShowQuotaBubble, true);
            var settings = new PetSettings { ShowQuotaBubble = false };
            Equal(JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(settings))!.ShowQuotaBubble, false);
        });
        Check("Weekly quota in primary slot, exact decimal credits", () =>
        {
            var data = Parse("""{"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":24,"windowDurationMins":10080,"resetsAt":1893499200},"secondary":null,"credits":{"balance":"1234.5678901234","unlimited":false}}}}""");
            Equal(data.Main?.Weekly?.RemainingPercent, 76d); Equal(data.CreditBalance, 1234.5678901234m);
            Equal(data.Main?.Weekly?.ResetsAt?.ToUnixTimeSeconds(), 1893499200L);
        });
        Check("Weekly quota in secondary slot; map takes priority over legacy", () =>
        {
            var data = Parse("""{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":10080}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":20,"windowDurationMins":300},"secondary":{"usedPercent":41,"windowDurationMins":10080}}}}""");
            Equal(data.Main?.Weekly?.RemainingPercent, 59d);
        });
        Check("Missing metrics remain unknown, including hasCredits false", () =>
        {
            var data = Parse("""{"rateLimits":{"primary":{"windowDurationMins":10080},"credits":{"hasCredits":false}}}""");
            Equal(data.Main?.Weekly?.RemainingPercent, null); Equal(data.CreditBalance, null);
        });
        Check("Short window is never mislabeled as a week", () =>
        { var data = Parse("""{"rateLimits":{"primary":{"usedPercent":25,"windowDurationMins":300}}}"""); Equal(data.Main?.Weekly, null); });
        Check("Spark quota is never substituted for main Codex quota", () =>
        { var data = Parse("""{"rateLimitsByLimitId":{"codex_spark":{"primary":{"usedPercent":2,"windowDurationMins":10080}}}}"""); Equal(data.Main, null); Equal(data.Buckets.Count, 1); });
        Check("Legacy-only response and unlimited credits", () =>
        { var data = Parse("""{"rateLimits":{"secondary":{"usedPercent":20,"windowDurationMins":10080},"credits":{"unlimited":true}},"rateLimitResetCredits":{"availableCount":0}}"""); Equal(data.Main?.Weekly?.RemainingPercent, 80d); Equal(data.UnlimitedCredits, true); Equal(data.ResetCredits, 0); });
        Check("Zero balance is a real zero, percentages stay within 0–100", () =>
        { var data = Parse("""{"rateLimits":{"primary":{"usedPercent":110,"windowDurationMins":10080},"credits":{"balance":0}}}"""); Equal(data.Main?.Weekly?.RemainingPercent, 0d); Equal(data.CreditBalance, 0m); });
        Check("Invalid timestamp and malformed optional fields are tolerated", () =>
        { var data = Parse("""{"rateLimits":{"primary":{"usedPercent":"bad","windowDurationMins":10080,"resetsAt":9223372036854775807},"credits":{"balance":"bad"}}}"""); Equal(data.Main?.Weekly?.ResetsAt, null); Equal(data.CreditBalance, null); });
        Check("All animation frames decode with transparency and valid durations", () =>
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "animations.json")));
            int total = 0;
            foreach (var group in manifest.RootElement.GetProperty("animations").EnumerateObject())
            {
                if (group.Value.GetArrayLength() == 0) throw new Exception("Empty animation: " + group.Name);
                foreach (var frame in group.Value.EnumerateArray())
                {
                    using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "assets", frame.GetProperty("file").GetString()!));
                    var decoded = new PngBitmapDecoder(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    if (decoded.Frames[0].PixelWidth <= 0) throw new Exception("Invalid PNG");
                    var rgba = new FormatConvertedBitmap(decoded.Frames[0], System.Windows.Media.PixelFormats.Bgra32, null, 0);
                    var corner = new byte[4]; rgba.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), corner, 4, 0);
                    if (corner[3] != 0) throw new Exception("Frame is missing a transparent corner");
                    if (frame.GetProperty("duration").GetInt32() <= 0) throw new Exception("Invalid duration");
                    total++;
                }
            }
            Equal(total, 111);
        });
        if (live)
        {
            Check("Live Codex Desktop root process is detected independently of CLI children", () =>
            { if (CodexFollower.DesktopInstances() is not { Length: > 0 }) throw new Exception("No desktop process detected"); });
            try
            {
                var data = await new CodexUsageClient().ReadAsync(CancellationToken.None);
                if (data.Main?.Weekly is null) throw new Exception("No weekly quota returned.");
                report.Add($"PASS Live Codex read: weekly used {data.Main.Weekly.UsedPercent}%, remaining {data.Main.Weekly.RemainingPercent}%, credits {data.CreditBalance}, fetched {data.FetchedAt:u}");
            }
            catch (Exception ex) { failures++; report.Add("FAIL Live Codex read: " + ex.Message); }
        }
        report.Add($"Result: {report.Count - failures} passed, {failures} failed.");
        Directory.CreateDirectory("artifacts");
        await File.WriteAllLinesAsync(Path.Combine("artifacts", "test-results.txt"), report);
        return failures > 0 ? 1 : 0;
    }
}
