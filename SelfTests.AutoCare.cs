using System;
using System.Linq;
using System.Text.Json;

namespace Momo;

internal static partial class SelfTests
{
    private static void RunAutoCareTests(Action<string, Action> check)
    {
        static void Require(bool ok) { if (!ok) throw new Exception("Auto-care expectation failed"); }
        static PetFood Meal(string name, double price, double hunger) => new(name, "Meal", "", null, "eat", price, 0, hunger, 0, 0, 0, 0);
        var bread = Meal("bread", 5, 20);
        check("Auto-care defaults on for legacy saves and preserves an explicit off choice", () =>
        {
            var legacy = JsonSerializer.Deserialize<PetSettings>("{\"Life\":{\"Coins\":123},\"CompletedFocus\":7}")!;
            Require(legacy.AutoCareEnabled && legacy.Life.Coins == 123 && legacy.CompletedFocus == 7);
            legacy.AutoCareEnabled = false;
            Require(!JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(legacy))!.AutoCareEnabled);
        });
        check("Auto-care triggers strictly below sixty, excluding affection and experience", () =>
        {
            var life = new PetLife { Hunger = 60, Affection = 0, Experience = 0 };
            Require(AutoCare.Select(life, new[] { bread }) is null);
            life.Hunger = 59.99; Require(AutoCare.Select(life, new[] { bread })?.Food == bread);
        });
        check("Auto-care chooses the cheapest affordable item that restores sixty", () =>
        {
            var life = new PetLife { Hunger = 45, Coins = 10 };
            Require(AutoCare.Select(life, new[] { Meal("large", 10, 70), Meal("small", 2, 5), bread })?.Food == bread);
            life.Coins = 4; Require(AutoCare.Select(life, new[] { bread, Meal("small", 2, 5), Meal("efficient", 3, 10) })?.Food.Name == "efficient");
        });
        check("Auto-care prioritizes the lowest restorable stat and skips unhelpful goods", () =>
        {
            var life = new PetLife { Hunger = 45, Thirst = 20 };
            var water = bread with { Name = "water", Hunger = 0, Thirst = 45 };
            Require(AutoCare.Select(life, new[] { bread, water })?.StatName == "水分");
            Require(AutoCare.Select(life, new[] { bread })?.StatName == "饱腹");
        });
        check("Auto-care rejects negative effects, invalid prices and zero-cost non-purchases", () =>
        {
            var life = new PetLife { Hunger = 20 };
            Require(AutoCare.Select(life, new[] { bread with { Health = -1 }, bread with { Price = double.NaN }, bread with { Price = -1 }, bread with { Price = 0 }, bread with { Energy = double.PositiveInfinity } }) is null);
        });
        check("Auto-care pays exactly once and respects cooldown and disabled or busy states", () =>
        {
            var settings = new PetSettings { Life = new PetLife { Hunger = 10, Coins = 30 } };
            var care = new AutoCare();
            Require(care.TryPurchase(settings, new[] { bread }, 0, false) is not null);
            Require(settings.Life.Coins == 25 && settings.Life.Hunger == 30);
            Require(care.TryPurchase(settings, new[] { bread }, 0, false) is null);
            Require(care.TryPurchase(settings, new[] { bread }, 29999, false) is null);
            Require(care.TryPurchase(settings, new[] { bread }, 30000, true) is null);
            settings.AutoCareEnabled = false; Require(care.TryPurchase(settings, new[] { bread }, 30000, false) is null);
            settings.AutoCareEnabled = true; Require(care.TryPurchase(settings, new[] { bread }, 30000, false) is not null);
            Require(settings.Life.Coins == 20 && settings.Life.Hunger == 50);
        });
        check("Auto-care cannot overspend and resumes when earned coins become sufficient", () =>
        {
            var settings = new PetSettings { Life = new PetLife { Hunger = 50, Coins = 4 } };
            var care = new AutoCare();
            Require(care.TryPurchase(settings, new[] { bread }, 0, false) is null && settings.Life.Coins == 4 && settings.Life.Hunger == 50);
            settings.Life.Coins = 5;
            Require(care.TryPurchase(settings, new[] { bread }, 30000, false) is not null && settings.Life.Coins == 0 && settings.Life.Hunger == 70);
            var restored = JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(settings))!;
            Require(new AutoCare().TryPurchase(restored, new[] { bread }, 0, false) is null && restored.Life.Hunger == 70);
        });
        check("Real shop has affordable non-harmful remedies for all five care stats", () =>
        {
            var catalog = new VPetCatalog();
            foreach (var lower in new Action<PetLife>[] { l => l.Energy = 40, l => l.Hunger = 40, l => l.Thirst = 40, l => l.Feeling = 40, l => l.Health = 40 })
            {
                var life = new PetLife { Coins = 1000 }; lower(life);
                var selected = AutoCare.Select(life, catalog.Foods);
                Require(selected is not null && catalog.Foods.Contains(selected.Food));
                double coins = life.Coins; Require(life.Feed(selected!.Food, out _) && life.Coins == coins - selected.Food.Price);
                Require(life.Energy >= 60 && life.Hunger >= 60 && life.Thirst >= 60 && life.Feeling >= 60 && life.Health >= 60);
            }
        });
    }
}
