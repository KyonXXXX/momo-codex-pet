using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Momo;

public sealed record QuotaWindow(double? UsedPercent, int? Minutes, DateTimeOffset? ResetsAt)
{
    public double? RemainingPercent => UsedPercent is double used ? Math.Clamp(100 - used, 0, 100) : null;
}
public sealed record QuotaBucket(string Id, string Name, string? Plan, QuotaWindow? Primary, QuotaWindow? Secondary)
{
    // A week can appear in either slot. Never assume that secondary is the weekly quota.
    public QuotaWindow? Weekly => new[] { Primary, Secondary }.FirstOrDefault(w => w?.Minutes == 10080);
}
public sealed record UsageSnapshot(DateTimeOffset FetchedAt, QuotaBucket? Main, IReadOnlyList<QuotaBucket> Buckets,
    decimal? CreditBalance, bool UnlimitedCredits, int? ResetCredits)
{
    public static UsageSnapshot Parse(JsonElement root, DateTimeOffset fetchedAt)
    {
        var buckets = new List<QuotaBucket>();
        JsonElement main = default;
        var map = Get(root, "rateLimitsByLimitId");
        if (map.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in map.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object) continue;
                buckets.Add(ParseBucket(entry.Value, entry.Name));
                if (entry.Name == "codex") main = entry.Value;
            }
        }
        var legacy = Get(root, "rateLimits");
        if (main.ValueKind != JsonValueKind.Object && legacy.ValueKind == JsonValueKind.Object &&
            (Text(legacy, "limitId") is null or "codex")) main = legacy;
        var mainBucket = main.ValueKind == JsonValueKind.Object ? ParseBucket(main, "codex") : null;
        if (mainBucket is not null && buckets.All(b => b.Id != "codex")) buckets.Insert(0, mainBucket);
        var credits = Get(main, "credits");
        // Some versions return credit details only in the legacy Codex bucket.
        if (credits.ValueKind != JsonValueKind.Object && (Text(legacy, "limitId") is null or "codex")) credits = Get(legacy, "credits");
        decimal? balance = null;
        var balanceValue = Get(credits, "balance");
        if (balanceValue.ValueKind == JsonValueKind.String && decimal.TryParse(balanceValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) balance = d;
        else if (balanceValue.ValueKind == JsonValueKind.Number && balanceValue.TryGetDecimal(out d)) balance = d;
        return new UsageSnapshot(fetchedAt, mainBucket, buckets, balance,
            Get(credits, "unlimited").ValueKind == JsonValueKind.True, Integer(Get(root, "rateLimitResetCredits"), "availableCount"));
    }
    private static QuotaBucket ParseBucket(JsonElement bucket, string id) => new(id, Text(bucket, "limitName") ?? (id == "codex" ? "Codex" : id),
        Text(bucket, "planType"), ParseWindow(Get(bucket, "primary")), ParseWindow(Get(bucket, "secondary")));
    private static QuotaWindow? ParseWindow(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object) return null;
        double? used = null;
        var v = Get(window, "usedPercent");
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n)) used = Math.Clamp(n, 0, 100);
        DateTimeOffset? resets = null;
        v = Get(window, "resetsAt");
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long epoch))
        { try { resets = DateTimeOffset.FromUnixTimeSeconds(epoch); } catch (ArgumentOutOfRangeException) { } }
        return new QuotaWindow(used, Integer(window, "windowDurationMins"), resets);
    }
    public static JsonElement Get(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var v) ? v : default;
    private static string? Text(JsonElement root, string key) => Get(root, key) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int? Integer(JsonElement root, string key) => Get(root, key) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
