using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Momo;

internal static class SelfTests
{
    public static async Task<int> RunAsync(bool live)
    {
        var report = new List<string>(); int failures = 0;
        void Check(string name, Action test) { try { test(); report.Add("PASS " + name); } catch (Exception ex) { failures++; report.Add("FAIL " + name + ": " + ex.Message); } }
        static void Equal<T>(T actual, T expected) { if (!Equals(actual, expected)) throw new Exception($"Expected {expected}, got {actual}"); }
        static UsageSnapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageSnapshot.Parse(doc.RootElement, DateTimeOffset.UtcNow); }
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
