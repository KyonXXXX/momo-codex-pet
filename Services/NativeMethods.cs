using System;
using System.Runtime.InteropServices;

namespace Momo;

internal static class NativeMethods
{
    public const int HotkeyMessage = 0x0312;
    public const int ShowMessage = 0x8000 + 77;
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    public static void ShowExisting() { var hwnd = FindWindow(null, "Momo · Codex 桌宠"); if (hwnd != IntPtr.Zero) PostMessage(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero); }
}
