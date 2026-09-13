using System;
using System.Collections.Generic;
using System.Linq;

namespace Momo;

public sealed record AutoCareChoice(string StatName, PetFood Food);

public sealed class AutoCare
{
    public const double Threshold = 60;
    public const long IntervalMs = 30000;
    private long _nextCheck = long.MinValue;
    private static readonly (string Name, Func<PetLife, double> Value, Func<PetFood, double> Gain)[] Stats =
    {
        ("健康", l => l.Health, f => f.Health),
        ("体力", l => l.Energy, f => f.Energy),
        ("饱腹", l => l.Hunger, f => f.Hunger),
        ("水分", l => l.Thirst, f => f.Thirst),
        ("心情", l => l.Feeling, f => f.Feeling)
    };

    public static AutoCareChoice? Select(PetLife life, IEnumerable<PetFood> foods)
    {
        if (!double.IsFinite(life.Coins) || life.Coins <= 0) return null;
        // Do not automatically trade away one care stat to restore another.
        var affordable = foods.Where(f => double.IsFinite(f.Price) && f.Price > 0 && f.Price <= life.Coins
            && Stats.All(s => double.IsFinite(s.Gain(f)) && s.Gain(f) >= 0)
            && double.IsFinite(f.Exp) && f.Exp >= 0 && double.IsFinite(f.Affection) && f.Affection >= 0).ToArray();
        foreach (var stat in Stats.Where(s => double.IsFinite(s.Value(life)) && s.Value(life) < Threshold).OrderBy(s => s.Value(life)))
        {
            double needed = Threshold - stat.Value(life);
            var food = affordable.Where(f => stat.Gain(f) > 0)
                .OrderBy(f => stat.Gain(f) >= needed ? 0 : 1)
                .ThenBy(f => stat.Gain(f) >= needed ? f.Price : f.Price / stat.Gain(f))
                .ThenBy(f => f.Price)
                .ThenBy(f => f.Name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (food is not null) return new(stat.Name, food);
        }
        return null;
    }

    // Called on the UI thread; selection, payment and restoration happen synchronously once.
    public AutoCareChoice? TryPurchase(PetSettings settings, IEnumerable<PetFood> foods, long nowMs, bool blocked)
    {
        if (!settings.AutoCareEnabled || blocked || nowMs < _nextCheck) return null;
        _nextCheck = nowMs + IntervalMs;
        var choice = Select(settings.Life, foods);
        return choice is not null && settings.Life.Feed(choice.Food, out _) ? choice : null;
    }
}
