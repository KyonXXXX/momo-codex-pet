using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Momo;

public sealed class CodexLaunchTracker
{
    private HashSet<string> _previous = new(StringComparer.Ordinal);
    public bool Observe(IEnumerable<string> instances, bool petRunning)
    {
        var current = instances.ToHashSet(StringComparer.Ordinal);
        bool start = current.Except(_previous).Any() && !petRunning;
        _previous = current;
        return start;
    }
}

internal static class CodexFollower
{
    internal const string RunName = "MomoCodexDesktopFollower";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WatchMutex = @"Local\MomoCodexDesktopFollower-v1";
    private const string StopEvent = @"Local\MomoCodexDesktopFollowerStop-v1";
    private const string PetMutex = @"Local\MomoCodexPet-v1";

    internal static bool IsDesktopPath(string path)
    {
        if (!string.Equals(Path.GetFileName(path), "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var app = Directory.GetParent(path);
        var package = app?.Parent;
        return string.Equals(app?.Name, "app", StringComparison.OrdinalIgnoreCase)
            && string.Equals(package?.Parent?.Name, "WindowsApps", StringComparison.OrdinalIgnoreCase)
            && package!.Name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
            && package.Name.EndsWith("__2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MutexExists(string name)
    {
        if (!Mutex.TryOpenExisting(name, out var mutex)) return false;
        mutex.Dispose(); return true;
    }

    internal static void Configure(bool enabled)
    {
        if (App.IsTestMode) return;
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (!enabled) { key.DeleteValue(RunName, false); return; }
        string exe = Environment.ProcessPath ?? throw new IOException("无法定位桌宠程序。");
        bool wasEnabled = Enabled();
        key.SetValue(RunName, $"\"{exe}\" --watch-codex");
        if (!wasEnabled || !MutexExists(WatchMutex)) Start(exe, "--watch-codex");
    }

    private static bool Enabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunName) is string command
            && command == $"\"{Environment.ProcessPath}\" --watch-codex";
    }

    private static void Start(string exe, string argument)
    {
        using var child = Process.Start(new ProcessStartInfo(exe)
        {
            Arguments = argument, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!, WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    internal static void StopForUpdate()
    {
        if (EventWaitHandle.TryOpenExisting(StopEvent, out var stop))
        { using (stop) stop.Set(); }
    }

    internal static async Task RunAsync()
    {
        using var mutex = new Mutex(false, WatchMutex);
        bool acquired;
        try { acquired = mutex.WaitOne(3000); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        try
        {
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEvent);
        stop.Reset();
        var tracker = new CodexLaunchTracker();
        while (!stop.WaitOne(0))
        {
            try
            {
                if (!Enabled()) return;
                var instances = DesktopInstances();
                if (instances is not null && tracker.Observe(instances, MutexExists(PetMutex)))
                    Start(Environment.ProcessPath!, "--codex-follow-start");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            { /* A later process scan can recover; never interact with Codex itself. */ }
            await Task.Delay(2000);
        }
        }
        finally { mutex.ReleaseMutex(); }
    }

    internal static string[]? DesktopInstances()
    {
        var parents = ParentProcesses();
        if (parents is null) return null;
        int session = Process.GetCurrentProcess().SessionId;
        var candidates = new Dictionary<int, string>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    if (process.SessionId == session && process.MainModule?.FileName is string path && IsDesktopPath(path))
                        candidates[process.Id] = $"{process.Id}:{process.StartTime.ToUniversalTime().Ticks}";
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
        // Ignore Electron renderer/GPU children; their restarts are not desktop launches.
        return candidates.Where(p => parents.TryGetValue(p.Key, out int parent) && !candidates.ContainsKey(parent)).Select(p => p.Value).ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private static Dictionary<int, int>? ParentProcesses()
    {
        var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot == new IntPtr(-1)) return null;
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>(), Name = "" };
            if (!Process32FirstW(snapshot, ref entry)) return null;
            var parents = new Dictionary<int, int>();
            do { parents[(int)entry.Id] = (int)entry.Parent; } while (Process32NextW(snapshot, ref entry));
            return parents;
        }
        finally { CloseHandle(snapshot); }
    }
}
