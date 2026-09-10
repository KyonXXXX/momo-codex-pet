using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace Momo;

public partial class App : Application
{
    private Mutex? _mutex;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MomoCodexPet");
    public static bool IsTestMode { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--demo") && !e.Args.Contains("--capture")) { Shutdown(2); return; }
        Directory.CreateDirectory(DataDirectory);
        IsTestMode = e.Args.Contains("--self-test") || e.Args.Contains("--capture");
        if (e.Args.Contains("--self-test"))
        {
            int result = await SelfTests.RunAsync(e.Args.Contains("--live"));
            Shutdown(result);
            return;
        }
        if (!IsTestMode)
        {
            _mutex = new Mutex(true, "Local\\MomoCodexPet-v1", out bool created);
            if (!created) { NativeMethods.ShowExisting(); Shutdown(); return; }
        }
        DispatcherUnhandledException += (_, args) =>
        {
            // No server output, authentication material, or conversation content is logged.
            File.AppendAllText(Path.Combine(DataDirectory, "error.log"), $"{DateTimeOffset.Now:u} {args.Exception.GetType().Name}: {args.Exception.StackTrace}\n");
            if (!IsTestMode) MessageBox.Show("桌宠遇到一个错误，请重新启动。诊断信息保存在 %LOCALAPPDATA%\\MomoCodexPet\\error.log。", "Momo 桌宠");
            args.Handled = true;
            Shutdown(1);
        };
        MainWindow = new PetWindow(e.Args);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
