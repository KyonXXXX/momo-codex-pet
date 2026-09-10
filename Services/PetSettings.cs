using System;
using System.IO;
using System.Text.Json;

namespace Momo;

public sealed class PetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1;
    public bool Compact { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool LowUsageNotification { get; set; } = true;
    public int CompletedFocus { get; set; }
    private static string FilePath => Path.Combine(App.DataDirectory, "settings.json");
    public static PetSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(FilePath)) ?? new();
            s.Scale = double.IsFinite(s.Scale) ? Math.Clamp(s.Scale, 0.75, 1.35) : 1;
            if (s.Left is double x && !double.IsFinite(x)) s.Left = null;
            if (s.Top is double y && !double.IsFinite(y)) s.Top = null;
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save()
    {
        if (App.IsTestMode) return;
        try { Directory.CreateDirectory(App.DataDirectory); File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this)); File.Move(FilePath + ".tmp", FilePath, true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
