using System;
using System.Text.Json;

namespace Momo;

internal static partial class SelfTests
{
    private static void RunCodexFollowerTests(Action<string, Action> check)
    {
        static void Require(bool condition) { if (!condition) throw new Exception("Codex follower expectation failed"); }
        check("Codex follow startup defaults enabled and retains a saved disabled preference", () =>
        {
            Require(JsonSerializer.Deserialize<PetSettings>("{}")!.FollowCodexDesktop);
            var settings = new PetSettings { FollowCodexDesktop = false, Life = new PetLife { Coins = 123 } };
            var restored = JsonSerializer.Deserialize<PetSettings>(JsonSerializer.Serialize(settings))!;
            Require(!restored.FollowCodexDesktop && restored.Life.Coins == 123);
        });
        check("Codex desktop identification supports package upgrades but excludes CLI and ChatGPT", () =>
        {
            Require(CodexFollower.IsDesktopPath(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.908.4834.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"));
            Require(CodexFollower.IsDesktopPath(@"D:\WindowsApps\OpenAI.Codex_99.0.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe"));
            Require(!CodexFollower.IsDesktopPath(@"C:\Users\test\AppData\Local\OpenAI\Codex\bin\version\codex.exe"));
            Require(!CodexFollower.IsDesktopPath(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_1_x64__2p2nqsd0c76g0\app\ChatGPT.exe"));
            Require(!CodexFollower.IsDesktopPath(@"C:\Other\ChatGPT.exe"));
        });
        check("Follower starts on desktop launch and respects manual pet exit within that session", () =>
        {
            var tracker = new CodexLaunchTracker();
            Require(!tracker.Observe(Array.Empty<string>(), false));
            Require(tracker.Observe(new[] { "100:1" }, false));
            Require(!tracker.Observe(new[] { "100:1" }, true));
            Require(!tracker.Observe(new[] { "100:1" }, false));
            Require(!tracker.Observe(Array.Empty<string>(), false));
            Require(tracker.Observe(new[] { "100:2" }, false));
        });
        check("Follower ignores repeated scans and avoids relaunching or revealing an existing pet", () =>
        {
            var tracker = new CodexLaunchTracker();
            Require(!tracker.Observe(new[] { "100:1" }, true));
            Require(!tracker.Observe(new[] { "100:1", "200:1" }, true));
            Require(!tracker.Observe(new[] { "100:1", "200:1" }, false));
            Require(tracker.Observe(new[] { "300:1" }, false));
        });
    }
}
