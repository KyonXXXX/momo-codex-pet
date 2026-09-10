using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Momo;

public partial class PetWindow : Window
{
    private readonly PetSettings _settings;
    private readonly CodexUsageClient _client = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(32) };
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private readonly AnimationPlayer _animation;
    private readonly Forms.NotifyIcon _tray;
    private readonly string[] _args;
    private UsageSnapshot? _usage;
    private bool _refreshing, _failed, _sleeping, _closing, _dragging;
    private DateTimeOffset? _focusEnd;
    private DateTimeOffset? _warnedReset;
    private Point? _mouseOrigin;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _hotkeyRegistered;
    private int _idleCount;

    public PetWindow(string[] args)
    {
        _args = args;
        _settings = App.IsTestMode ? new() : PetSettings.Load();
        InitializeComponent();
        Topmost = _settings.AlwaysOnTop;
        _animation = new AnimationPlayer(PetImage, _catalog);
        _animation.Play("idle");
        // Recheck only when the pose group or visible controls change, never on every frame.
        _animation.BoundsChanged += QueueBoundsCheck;
        SpeechBubble.IsVisibleChanged += (_, _) => QueueBoundsCheck();
        FocusBadge.IsVisibleChanged += (_, _) => QueueBoundsCheck();
        _tray = new Forms.NotifyIcon { Text = "Momo · Codex 桌宠", Icon = CreateTrayIcon(), Visible = !App.IsTestMode };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowPet);
        var trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("显示桌宠", null, (_, _) => Dispatcher.Invoke(ShowPet));
        trayMenu.Items.Add("立即刷新额度", null, (_, _) => Dispatcher.InvokeAsync(async () => await RefreshAsync()));
        var bubbleToggle = new Forms.ToolStripMenuItem("显示额度气泡");
        bubbleToggle.Click += (_, _) => Dispatcher.Invoke(ToggleQuotaBubble);
        trayMenu.Opening += (_, _) => bubbleToggle.Checked = _settings.ShowQuotaBubble;
        trayMenu.Items.Add(bubbleToggle);
        trayMenu.Items.Add("回到屏幕右下角", null, (_, _) => Dispatcher.Invoke(() => { ShowPet(); ResetPosition(); }));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("互动与动画图鉴", null, (_, _) => Dispatcher.Invoke(ShowInteractionPanel));
        trayMenu.Items.Add("停止当前动作", null, (_, _) => Dispatcher.Invoke(StopPetActivity));
        trayMenu.Items.Add("退出 Momo", null, (_, _) => Dispatcher.Invoke(ExitWithAnimation));
        _tray.ContextMenuStrip = trayMenu;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _clockTimer.Tick += (_, _) => TickClock();
        _idleTimer.Tick += (_, _) => PetIdleAction();
        _bubbleTimer.Tick += (_, _) => { SpeechBubble.Visibility = Visibility.Collapsed; _bubbleTimer.Stop(); };
        Loaded += OnLoaded;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_hwnd); _source?.AddHook(WindowProc);
            if (!App.IsTestMode) _hotkeyRegistered = NativeMethods.RegisterHotKey(_hwnd, 1, 0x4000 | 0x0002 | 0x0001, 0x4D); // Ctrl+Alt+M, no repeat
        };
        Closed += (_, _) => Cleanup();
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplySize();
        if (_settings.Left is double x && _settings.Top is double y) { Left = x; Top = y; ClampPosition(); }
        else ResetPosition();
        _refreshTimer.Start(); _clockTimer.Start(); _idleTimer.Start();
        InitializeInteractions();
        if (_args.Contains("--demo")) { _usage = DemoUsage(); RenderUsage(); }
        else await RefreshAsync();
        if (_args.Contains("--capture")) await CaptureAsync();
        else PetStartup();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _closing || _args.Contains("--demo")) return;
        _refreshing = true; RefreshButton.IsEnabled = false;
        if (_usage is null) StatusText.Text = "连接中";
        try
        {
            var snapshot = await _client.ReadAsync(_lifetime.Token);
            if (_closing) return;
            _usage = snapshot; _failed = false; QuotaCard.ToolTip = null;
            RenderUsage();
            if (_settings.LowUsageNotification && snapshot.Main?.Weekly is { RemainingPercent: <= 15, ResetsAt: not null } week && _warnedReset != week.ResetsAt)
            {
                _warnedReset = week.ResetsAt;
                Say($"周剩 {week.RemainingPercent:0.#}%，慢慢来。");
                Notify("Codex 额度提醒", $"本周剩余 {week.RemainingPercent:0.#}%。余额 {CreditLabel(snapshot)} credits。");
            }
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex)
        {
            if (_closing) return;
            _failed = true;
            StatusText.Text = _usage is null ? "未连接" : "离线";
            StatusText.ToolTip = _usage is null ? "连接失败，确认 Codex 已登录后刷新。" : $"显示上次数据：{_usage.FetchedAt.ToLocalTime():MM/dd HH:mm:ss}";
            StatusDot.Fill = Brush("#C29A5E");
            QuotaCard.ToolTip = ex is UsageException ? ex.Message : "暂时无法读取额度，60 秒后自动重试。";
            if (_usage is null) ResetText.Text = "登录 Codex 后刷新 ↻";
        }
        finally { _refreshing = false; if (!_closing) RefreshButton.IsEnabled = true; }
    }

    private static string CreditLabel(UsageSnapshot usage) => usage.UnlimitedCredits ? "不限" : usage.CreditBalance?.ToString("N2", CultureInfo.InvariantCulture) ?? "未提供";
    private void RenderUsage()
    {
        if (_usage is not { } data) return;
        var weekly = data.Main?.Weekly;
        RemainingText.Text = weekly?.RemainingPercent?.ToString("0.#", CultureInfo.InvariantCulture) ?? "—";
        UsedText.Text = weekly?.UsedPercent is double used ? $"已用 {used:0.#}%" : "已用 —%";
        PlanText.Text = (data.Main?.Plan ?? "CODEX").ToUpperInvariant();
        PlanText.ToolTip = PlanText.Text;
        CreditsText.Text = CreditLabel(data);
        CreditsText.ToolTip = data.UnlimitedCredits ? "接口返回不限量 credits。" : data.CreditBalance is decimal credit ? $"精确余额：{credit.ToString(CultureInfo.InvariantCulture)} credits\ncredits 是服务额度单位，并非货币余额。" : "服务没有返回余额；未知数据不会显示为零。";
        UsageFill.Width = 138 * (weekly?.UsedPercent ?? 0) / 100;
        UsageFill.Background = Brush(weekly?.RemainingPercent <= 15 ? "#D9A26E" : "#BA98DA");
        RemainingText.Foreground = Brush(weekly?.RemainingPercent <= 15 ? "#B78147" : "#775696");
        if (weekly?.ResetsAt is DateTimeOffset reset)
        {
            ResetText.Text = $"重置 {reset.ToLocalTime():MM/dd HH:mm}";
            ResetText.ToolTip = $"{reset.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}\n按电脑本地时区显示。周额度是账户的 7 天窗口。";
        }
        else { ResetText.Text = "重置时间未提供"; ResetText.ToolTip = null; }
        UpdateFreshness();
        var trayText = $"Momo · 本周剩余 {RemainingText.Text}% · {CreditLabel(data)} credits";
        _tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    private void UpdateFreshness()
    {
        if (_usage is null) return;
        var age = DateTimeOffset.UtcNow - _usage.FetchedAt;
        bool stale = _failed || age > TimeSpan.FromMinutes(2);
        StatusText.Text = stale ? "离线" : "已同步";
        StatusText.ToolTip = $"{(stale ? "离线，显示上次数据。\n" : "")}最近成功同步：{_usage.FetchedAt.ToLocalTime():MM/dd HH:mm:ss}\n每 60 秒自动读取，不调用模型。";
        StatusDot.Fill = Brush(stale ? "#C29A5E" : "#87AE97");
    }

    private void TickClock()
    {
        TickPetLife();
        UpdateFreshness();
        if (_focusEnd is not DateTimeOffset end) return;
        var left = end - DateTimeOffset.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            _focusEnd = null; _settings.CompletedFocus++; _settings.Save();
            FocusBadge.Visibility = Visibility.Collapsed; FocusButton.Content = "◷ 专注";
            Say("专注完成，伸个懒腰 ♡");
            Notify("专注完成 ♡", "辛苦啦，休息 5 分钟，再开始下一段吧。");
            PlayReturn("focusOut");
        }
        else FocusTimeText.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
    }

    private void IdleAction()
    {
        if (_sleeping || _focusEnd is not null || _dragging || !IsVisible || _animation.Current != "idle") return;
        _idleCount++;
        PlayReturn("meow");
        if (_idleCount % 3 == 0) Say(new[] { "我陪你，慢慢来。", "记得喝水哦。", "摸摸我，充个电 ♡" }[(_idleCount / 3) % 3]);
    }
    private void PlayReturn(string animation) => _animation.Play(animation, false, ReturnToState);
    private void ReturnToState()
    {
        if(_actionFamily is not null)return;
        if(_focusEnd is not null){_animation.Play("focus");return;}
        var clip=_catalog.Choose(_sleeping?"Sleep":"Default",Mood,_sleeping?"B":"Single");
        if(clip is not null)_animation.Play("@"+clip.Id);else _animation.Play("idle");
    }
    private void Pat()
    {
        PerformFamily("Touch_Head");
        Say(new[] { "诶嘿，摸摸 ♡", "充电完成！", "今天也很努力呢。" }[Random.Shared.Next(3)],false);
    }
    private void Say(string text, bool animate = true) { SpeechText.Text = text; SpeechBubble.Visibility = Visibility.Visible; _bubbleTimer.Stop(); _bubbleTimer.Start(); }
    private void Notify(string title, string body)
    { if (!App.IsTestMode) _tray.ShowBalloonTip(5000, title, body, Forms.ToolTipIcon.Info); }
    private void PatClick(object sender, RoutedEventArgs e) => Pat();
    private void FocusClick(object sender, RoutedEventArgs e)
    {
        CancelInteraction();FocusCaption.Text="陪你专注";
        if (_focusEnd is not null)
        {
            _focusEnd = null; FocusBadge.Visibility = Visibility.Collapsed; FocusButton.Content = "◷ 专注";
            Say("歇一下，再继续。"); PlayReturn("focusOut"); return;
        }
        _sleeping = false; SleepButton.Content = "☾ 休息";
        _focusEnd = DateTimeOffset.UtcNow.AddMinutes(25); FocusTimeText.Text = "25:00";
        FocusBadge.Visibility = Visibility.Visible; FocusButton.Content = "□ 结束";
        Say("陪你专注 25 分钟。"); _animation.Play("focusIn", false, ReturnToState);
    }
    private void SleepClick(object sender, RoutedEventArgs e)
    {
        CancelInteraction();
        _sleeping = !_sleeping;
        if (_sleeping && _focusEnd is not null) { _focusEnd = null; FocusBadge.Visibility = Visibility.Collapsed; FocusButton.Content = "◷ 专注"; }
        SleepButton.Content = _sleeping ? "☀ 唤醒" : "☾ 休息";
        Say(_sleeping ? "眯一会儿，额度照常更新。" : "醒啦，继续加油 ♡");
        StartAction("Sleep",_sleeping?0:1,ReturnToState,prepare:false);
        if(!_sleeping)EndAction();
    }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void CardDrag(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is not null) return;
        DragSafely();
    }
    private static T? FindParent<T>(DependencyObject? obj) where T : DependencyObject
    { while (obj is not null) { if (obj is T match) return match; obj = VisualTreeHelper.GetParent(obj); } return null; }
    private void PetMouseDown(object sender, MouseButtonEventArgs e) { _mouseOrigin = e.GetPosition(this); PetImage.CaptureMouse(); e.Handled = true; }
    private void PetMouseMove(object sender, MouseEventArgs e)
    {
        if (_mouseOrigin is not Point origin || e.LeftButton != MouseButtonState.Pressed) return;
        if ((e.GetPosition(this) - origin).Length < 5) return;
        _mouseOrigin = null; PetImage.ReleaseMouseCapture(); DragSafely();
    }
    private void PetMouseUp(object sender, MouseButtonEventArgs e)
    { bool click = _mouseOrigin is not null; var point=e.GetPosition(PetImage); _mouseOrigin = null; PetImage.ReleaseMouseCapture(); if (click) TouchAt(point); e.Handled = true; }
    private void DragSafely()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed) return;
        _dragging = true;
        BeginRaisedDrag();
        try { DragMove(); } catch (InvalidOperationException) { }
        finally { _dragging = false; EndRaisedDrag(); ClampPosition(); SavePosition(); }
    }

    private void ApplySize()
    {
        double spriteSize = _settings.Compact ? 192 : 256;
        PetStage.Width = PetStage.Height = PetImage.Width = PetImage.Height = spriteSize;
        double bubbleSpace = _settings.ShowQuotaBubble ? 108 : 0;
        Root.Width = Math.Max(spriteSize, 220);
        double spriteLeft = (Root.Width - spriteSize) / 2;
        PetStage.Margin = new Thickness(spriteLeft, 8 + bubbleSpace, 0, 0);
        QuotaBubble.Visibility = _settings.ShowQuotaBubble ? Visibility.Visible : Visibility.Collapsed;
        QuotaBubble.Margin = new Thickness((Root.Width - 160) / 2, 6, 0, 0);
        SpeechBubble.Margin = new Thickness(0, 134 + bubbleSpace, 6, 0);
        FocusBadge.Margin = new Thickness(0, Math.Max(180, spriteSize - 58) + bubbleSpace, 6, 0);
        Root.Height = spriteSize + 46 + bubbleSpace;
        var area = WorkingArea();
        double scale = Math.Min(_settings.Scale, Math.Min((area.Width - 20) / Root.Width, (area.Height - 20) / Root.Height));
        WindowScale.ScaleX = scale; WindowScale.ScaleY = scale;
        Width = Root.Width * scale; Height = Root.Height * scale;
        ClampPosition();
    }
    private void ToggleQuotaBubble()
    {
        double petTop = Top + PetStage.Margin.Top * WindowScale.ScaleY;
        _settings.ShowQuotaBubble = !_settings.ShowQuotaBubble;
        ApplySize();
        // Keep the character in place while the space above it opens or closes.
        Top = petTop - PetStage.Margin.Top * WindowScale.ScaleY;
        ClampPosition(); SavePosition();
    }
    private Rect WorkingArea()
    {
        var screen = _hwnd == IntPtr.Zero ? Forms.Screen.PrimaryScreen! : Forms.Screen.FromHandle(_hwnd);
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(screen.WorkingArea.X / dpi.DpiScaleX, screen.WorkingArea.Y / dpi.DpiScaleY, screen.WorkingArea.Width / dpi.DpiScaleX, screen.WorkingArea.Height / dpi.DpiScaleY);
    }
    private void ResetPosition()
    { UpdateLayout(); var area = WorkingArea(); Left = area.Right - VisibleHorizontalBounds().Right * WindowScale.ScaleX - 22; Top = area.Bottom - Height - 14; ClampPosition(); SavePosition(); }
    private void ClampPosition()
    {
        UpdateLayout();
        var area = WorkingArea();
        var bounds = VisibleHorizontalBounds();
        double minLeft = area.Left - bounds.Left * WindowScale.ScaleX;
        double maxLeft = area.Right - bounds.Right * WindowScale.ScaleX;
        if (double.IsNaN(Left)) Left = area.Right - Width;
        if (double.IsNaN(Top)) Top = area.Bottom - Height;
        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
    }
    private void QueueBoundsCheck()
    {
        if (IsLoaded && !_closing)
            Dispatcher.InvokeAsync(() => { if (!_closing && !_dragging) ClampPosition(); }, DispatcherPriority.Loaded);
    }
    private Rect VisibleHorizontalBounds()
    {
        // PNG canvas padding is not a desktop boundary. Use the union of the current
        // animation's opaque pixels so ordinary frame changes do not shift the pet.
        var pose = _animation.VisibleBounds;
        if(pose.IsEmpty)pose=new Rect(.5,.5,0,0);
        double petLeft = PetStage.Margin.Left;
        var bounds = new Rect(petLeft + pose.Left * PetStage.Width, 0, pose.Width * PetStage.Width, Root.Height);
        bounds.Union(new Rect(petLeft + (PetStage.Width - 104) / 2, 0, 104, Root.Height));
        foreach (var element in new FrameworkElement[] { ActionBar, QuotaBubble, SpeechBubble, FocusBadge })
        {
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0) continue;
            var origin = element.TranslatePoint(new Point(), Root);
            double shadow = element == QuotaBubble ? 6 : 0;
            bounds.Union(new Rect(origin.X - shadow, 0, element.ActualWidth + shadow * 2, Root.Height));
        }
        bounds.Inflate(2, 0);
        return bounds;
    }
    private void SavePosition() { _settings.Left = Left; _settings.Top = Top; _settings.Save(); }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() => { if (!_closing) { ApplySize(); SavePosition(); } });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) Dispatcher.InvokeAsync(async () => { if (!_closing) { TickClock(); await RefreshAsync(); } }); }

    private void MenuClick(object sender, RoutedEventArgs e) => OpenMenu();
    private void ShowMenu(object sender, MouseButtonEventArgs e) { OpenMenu(); e.Handled = true; }
    private ContextMenu OpenMenu()
    {
        var menu = new ContextMenu();
        Add(menu, "互动与动画图鉴", ShowInteractionPanel);
        Add(menu, "停止当前动作", StopPetActivity);
        menu.Items.Add(new Separator());
        Add(menu, "立即刷新额度", async () => await RefreshAsync());
        Add(menu, "查看所有额度与同步详情", ShowDetails);
        menu.Items.Add(new Separator());
        Add(menu, "显示额度气泡", ToggleQuotaBubble, _settings.ShowQuotaBubble);
        Add(menu, "始终置顶", () => { _settings.AlwaysOnTop = !Topmost; Topmost = _settings.AlwaysOnTop; _settings.Save(); }, Topmost);
        Add(menu, "小巧模式", () => { _settings.Compact = !_settings.Compact; ApplySize(); SavePosition(); }, _settings.Compact);
        var sizeMenu = new MenuItem { Header = "桌宠大小" };
        foreach (var scale in new[] { 0.8, 1.0, 1.2 })
        { var value = scale; var item = new MenuItem { Header = $"{scale:P0}", IsCheckable = true, IsChecked = Math.Abs(_settings.Scale - scale) < .01 }; item.Click += (_, _) => { _settings.Scale = value; ApplySize(); SavePosition(); }; sizeMenu.Items.Add(item); }
        menu.Items.Add(sizeMenu);
        Add(menu, "周额度不足提醒（剩余 ≤15%）", () => { _settings.LowUsageNotification = !_settings.LowUsageNotification; _settings.Save(); }, _settings.LowUsageNotification);
        Add(menu, "开机启动", ToggleAutoStart, AutoStartEnabled());
        Add(menu, "回到屏幕右下角", ResetPosition);
        menu.Items.Add(new Separator());
        Add(menu, "素材来源与使用说明", ShowAbout);
        Add(menu, "隐藏到托盘" + (_hotkeyRegistered ? "    Ctrl+Alt+M" : ""), () => { Hide(); _animation.Pause(true); });
        Add(menu, "退出桌宠", ExitWithAnimation);
        menu.PlacementTarget = QuotaBubble.IsVisible ? (UIElement)MenuButton : PetImage;
        menu.IsOpen = true;
        return menu;
    }
    private static void Add(ContextMenu menu, string title, Action action, bool? check = null)
    { var item = new MenuItem { Header = title }; if (check is bool enabled) { item.IsCheckable = true; item.IsChecked = enabled; } item.Click += (_, _) => action(); menu.Items.Add(item); }
    private static bool AutoStartEnabled()
    { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("MomoCodexPet") is string; }
    private void ToggleAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (AutoStartEnabled()) { key.DeleteValue("MomoCodexPet", false); Say("已关闭开机启动。"); }
            else { key.SetValue("MomoCodexPet", $"\"{Environment.ProcessPath}\""); Say("下次开机，我会自动来陪你。"); }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException) { Say("开机启动设置失败，请稍后重试。"); }
    }
    private void ShowPet() { Show(); _animation.Pause(false); ClampPosition(); }
    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.ShowMessage) { ShowPet(); handled = true; }
        if (message == NativeMethods.HotkeyMessage && wParam.ToInt32() == 1)
        { if (IsVisible) { Hide(); _animation.Pause(true); } else ShowPet(); handled = true; }
        return IntPtr.Zero;
    }
    private void ShowDetails()
    {
        var text = new StringBuilder("账户额度\n\n");
        if (_usage is not { } data) text.Append("尚未读取成功。请在 Codex 中登录 ChatGPT 账号，然后点击刷新。\n");
        else
        {
            text.AppendLine($"余额：{(data.UnlimitedCredits ? "不限量" : data.CreditBalance?.ToString(CultureInfo.InvariantCulture) ?? "未提供")} credits");
            text.AppendLine($"可用额度重置次数：{data.ResetCredits?.ToString() ?? "未提供"}\n");
            foreach (var bucket in data.Buckets)
            {
                text.AppendLine(bucket.Name);
                foreach (var window in new[] { bucket.Primary, bucket.Secondary }.Where(w => w is not null).Cast<QuotaWindow>())
                {
                    string span = window.Minutes == 10080 ? "每周" : window.Minutes == 300 ? "5 小时" : $"{window.Minutes?.ToString() ?? "未知"} 分钟";
                    text.AppendLine($"  {span}：已用 {window.UsedPercent?.ToString("0.#") ?? "—"}%，剩余 {window.RemainingPercent?.ToString("0.#") ?? "—"}%");
                    text.AppendLine($"  重置：{window.ResetsAt?.ToLocalTime().ToString("MM/dd HH:mm zzz") ?? "未提供"}");
                }
                text.AppendLine();
            }
            text.AppendLine($"最近同步：{data.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
            text.AppendLine($"数据状态：{(_failed || DateTimeOffset.UtcNow - data.FetchedAt > TimeSpan.FromMinutes(2) ? "离线，显示上次数据" : "已同步")}");
        }
        text.AppendLine("\n每 60 秒读取本机 Codex App Server 的账户额度。\n不发送模型请求，不消耗模型额度。\n周窗口以服务返回的重置时间为准。\ncredits 为服务额度单位，不是美元或人民币。");
        text.AppendLine($"\n已完成专注：{_settings.CompletedFocus} 次");
        ShowTextWindow("额度与同步详情", text.ToString());
    }
    private void ShowAbout()
    {
        string attribution = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "licenses", "VPet-Animation-License.zh-CN.md"));
        ShowTextWindow("关于 Momo", "MOMO · 你的 Codex 小伴侣\n\n点击角色或「摸摸」互动，拖动角色或卡片改变位置。\n「专注」开启 25 分钟计时；「休息」仍然刷新额度。\n右键可调整大小、置顶和开机启动。\nCtrl+Alt+M 显示 / 隐藏；托盘双击也可找回。\n\n角色动画来自 VPet，版权所有：虚拟主播模拟器制作组。\nhttps://github.com/LorisYounger/VPet\n\n" + attribution, true);
    }
    private void ShowTextWindow(string title, string text, bool link = false)
    {
        var window = new Window { Title = title, Width = 440, Height = 580, MinWidth = 380, MinHeight = 360, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = Brush("#FFFCFF"), FontFamily = new FontFamily("Microsoft YaHei UI"), Topmost = Topmost };
        var panel = new DockPanel { Margin = new Thickness(24) };
        if (link)
        {
            var button = new Button { Content = "打开 VPet 素材来源", Margin = new Thickness(0, 12, 0, 0) };
            button.Click += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/LorisYounger/VPet") { UseShellExecute = true });
            DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button);
        }
        panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 23 } });
        window.Content = panel; window.Show();
    }
    private static SolidColorBrush Brush(string hex) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; brush.Freeze(); return brush; }
    private static System.Drawing.Icon CreateTrayIcon()
    {
        // Tray icon is drawn locally. Character artwork remains in its original PNG frames.
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.Transparent);
        using var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(158, 121, 192));
        g.FillEllipse(bg, 1, 1, 30, 30);
        using var white = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        g.FillPolygon(white, new[] { new System.Drawing.PointF(16,5),new System.Drawing.PointF(19,13),new System.Drawing.PointF(27,16),new System.Drawing.PointF(19,19),new System.Drawing.PointF(16,27),new System.Drawing.PointF(13,19),new System.Drawing.PointF(5,16),new System.Drawing.PointF(13,13) });
        var handle = bmp.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

    private async Task CaptureAsync()
    {
        int index = Array.IndexOf(_args, "--capture");
        string path = index + 1 < _args.Length ? _args[index + 1] : Path.Combine(App.DataDirectory, "preview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var testReport = new List<string>();
        void Assert(bool condition, string description)
        {
            testReport.Add((condition ? "PASS " : "FAIL ") + description);
            if (!condition)
            {
                File.WriteAllLines(Path.ChangeExtension(path, ".txt"), testReport);
                throw new InvalidOperationException("UI integration test failed: " + description);
            }
        }
        // Exercise the actual routed button events and window message handler.
        FocusButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(_focusEnd is not null && FocusBadge.IsVisible && _animation.Current == "focusIn", "focus button starts timer and entry animation");
        SleepButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(_sleeping && _focusEnd is null && !FocusBadge.IsVisible, "sleep cancels focus and keeps quota card visible");
        Assert(CreditsText.IsVisible && RemainingText.IsVisible, "weekly quota and balance remain visible during sleep");
        PatButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(!_sleeping && _animation.Current.StartsWith("@Touch_Head/"), "pat button wakes character and plays head-pat animation");
        FocusButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        int previousFocus = _settings.CompletedFocus;
        _focusEnd = DateTimeOffset.UtcNow.AddSeconds(-1); TickClock(); TickClock();
        Assert(_focusEnd is null && _settings.CompletedFocus == previousFocus + 1, "focus completion fires once using wall-clock deadline");
        bool handled = false;
        WindowProc(_hwnd, NativeMethods.HotkeyMessage, new IntPtr(1), IntPtr.Zero, ref handled);
        Assert(!IsVisible && handled, "show/hide hotkey handler hides window");
        WindowProc(_hwnd, NativeMethods.ShowMessage, IntPtr.Zero, IntPtr.Zero, ref handled);
        Assert(IsVisible, "single-instance/tray restore route shows window");
        Left = -100000; Top = -100000; ClampPosition();
        var visibleBounds = VisibleHorizontalBounds();
        Assert(WorkingArea().Contains(new Rect(Left + visibleBounds.Left * WindowScale.ScaleX, Top, visibleBounds.Width * WindowScale.ScaleX, Height)), "off-screen position keeps visible content within monitor work area");
        ResetPosition();
        var liveUsage = _usage;
        var previousCredits = CreditsText.Text;
        _failed = true; UpdateFreshness();
        Assert(StatusText.Text.Contains("离线") && CreditsText.Text == previousCredits, "failed refresh retains last balance and labels it offline");
        _failed = false;
        if (_usage is not null)
        {
            _usage = _usage with { FetchedAt = DateTimeOffset.UtcNow.AddMinutes(-3) }; UpdateFreshness();
            Assert(StatusText.Text.Contains("离线"), "data older than two minutes is marked stale");
        }
        using (var empty = System.Text.Json.JsonDocument.Parse("{}")) _usage = UsageSnapshot.Parse(empty.RootElement, DateTimeOffset.UtcNow);
        RenderUsage();
        Assert(RemainingText.Text == "—" && CreditsText.Text == "未提供", "missing live metrics remain unknown instead of zero");
        _usage = liveUsage; RenderUsage();
        Assert(_usage?.Main?.Weekly is not null && !_failed, _args.Contains("--demo") ? "synthetic demo data available for public screenshots" : "live Codex data available for local verification");
        Assert(QuotaCard.ActualWidth == 160 && QuotaCard.ActualHeight == 100, "quota bubble is 160 x 100 logical pixels");
        var cardOrigin = QuotaCard.TransformToAncestor(Root).Transform(new Point());
        Assert(cardOrigin.Y + QuotaCard.ActualHeight <= PetStage.Margin.Top && Math.Abs(cardOrigin.X + 80 - (PetStage.Margin.Left + PetStage.Width / 2)) < 1, "quota bubble is centered above the character without covering it");
        Assert(_settings.ShowQuotaBubble && QuotaBubble.IsVisible, "quota bubble is enabled by default");
        double petScreenTop = Top + PetStage.Margin.Top * WindowScale.ScaleY;
        double visibleHeight = Root.Height;
        var toggleMenu = OpenMenu();
        var toggleItem = toggleMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "显示额度气泡"));
        toggleItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); toggleMenu.IsOpen = false; UpdateLayout();
        Assert(!QuotaBubble.IsVisible && !_settings.ShowQuotaBubble && Root.Height == visibleHeight - 108, "menu hides bubble and reclaims its space");
        Assert(Math.Abs(Top + PetStage.Margin.Top * WindowScale.ScaleY - petScreenTop) < 1, "hiding bubble keeps character in place");
        toggleMenu = OpenMenu();
        Assert(toggleMenu.PlacementTarget == PetImage, "settings menu remains accessible on character with bubble hidden");
        toggleItem = toggleMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "显示额度气泡"));
        Assert(!toggleItem.IsChecked, "hidden bubble menu reflects saved choice");
        toggleItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); toggleMenu.IsOpen = false; UpdateLayout();
        Assert(QuotaBubble.IsVisible && _settings.ShowQuotaBubble && Root.Height == visibleHeight, "character menu restores quota bubble");
        Assert(UsageFill.ActualWidth <= UsageTrack.ActualWidth, "usage bar remains inside its resized track");
        _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed;
        foreach (string state in new[] { "idle", "pat", "focus", "sleep" })
        {
            _animation.Play(state); _animation.Pause(true);
            // Capture a representative middle pose instead of just the entry pose.
            if (state == "pat") PetImage.Source = LoadCaptureFrame("vpet/pat/008.png");
            SpeechText.Text = state switch { "pat" => "诶嘿，摸摸 ♡", "focus" => "陪你专注 25 分钟。", "sleep" => "眯一会儿…", _ => "加油呀 ♡" };
            SpeechBubble.Visibility = state == "pat" ? Visibility.Visible : Visibility.Collapsed;
            if (state == "focus") { FocusBadge.Visibility = Visibility.Visible; FocusTimeText.Text = "24:59"; }
            else FocusBadge.Visibility = Visibility.Collapsed;
            UpdateLayout(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap((int)Root.ActualWidth * 2, (int)Root.ActualHeight * 2, 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(Root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var file = state == "idle" ? path : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "-" + state + ".png");
            using (var stream = File.Create(file)) encoder.Save(stream);
            testReport.Add($"{state}: animation rendered {bitmap.PixelWidth}x{bitmap.PixelHeight}");
        }
        _settings.Compact = true; ApplySize(); UpdateLayout();
        Assert(QuotaCard.ActualWidth == 160 && QuotaCard.ActualHeight == 100, "compact mode retains readable bubble size");
        testReport.Add($"compact: window {Width}x{Height}; credits visible={CreditsText.IsVisible}; weekly visible={RemainingText.IsVisible}");
        foreach (bool compact in new[] { false, true })
        foreach (double scale in new[] { .8, 1.0, 1.2 })
        foreach (bool bubble in new[] { false, true })
        foreach (string state in new[] { "idle", "pat", "focus", "sleep" })
        {
            _settings.Compact = compact; _settings.Scale = scale; _settings.ShowQuotaBubble = bubble;
            _animation.Play(state); _animation.Pause(true);
            FocusBadge.Visibility = state == "focus" ? Visibility.Visible : Visibility.Collapsed;
            SpeechBubble.Visibility = state == "pat" ? Visibility.Visible : Visibility.Collapsed;
            ApplySize(); UpdateLayout();
            Left = WorkingArea().Right; ClampPosition();
            var edge = VisibleHorizontalBounds(); var area = WorkingArea();
            Assert(Math.Abs(Left + edge.Right * WindowScale.ScaleX - area.Right) < 1, $"right edge: {state}, compact={compact}, scale={scale}, bubble={bubble}");
            if (state == "idle")
            {
                var edgeBitmap = new RenderTargetBitmap((int)Math.Ceiling(Width * 2), (int)Math.Ceiling(Height * 2), 192, 192, PixelFormats.Pbgra32);
                edgeBitmap.Render(Root);
                var opaque = AnimationPlayer.OpaqueBounds(edgeBitmap);
                double visibleRight = Left + opaque.Right * edgeBitmap.PixelWidth / 2;
                Assert(area.Right - visibleRight >= -1 && area.Right - visibleRight <= 4 * WindowScale.ScaleX, $"rendered pixels reach right screen edge within 4px: compact={compact}, scale={scale}, bubble={bubble}, gap={area.Right - visibleRight:0.##}");
            }
            Left = area.Left - Width; ClampPosition();
            Assert(Math.Abs(Left + edge.Left * WindowScale.ScaleX - area.Left) < 1, $"left edge: {state}, compact={compact}, scale={scale}, bubble={bubble}");
        }
        if (_args.Contains("--verify-auto-refresh") && !_args.Contains("--demo"))
        {
            var last = _usage!.FetchedAt;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(80);
            while (_usage.FetchedAt <= last && DateTimeOffset.UtcNow < deadline) await Task.Delay(500);
            Assert(_usage.FetchedAt > last && !_failed, $"automatic 60-second timer obtained a new live reading at {_usage.FetchedAt:u}");
        }
        testReport.Add($"{(_args.Contains("--demo") ? "demo" : "live")}: remaining={RemainingText.Text}%, balance={CreditsText.Text}, status={StatusText.Text}");
        if(_args.Contains("--full-test"))await TestFullInteractionsAsync(Assert,Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllLinesAsync(Path.ChangeExtension(path, ".txt"), testReport);
        Close();
    }
    private static BitmapImage LoadCaptureFrame(string relativePath)
    { var bitmap = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "assets", relativePath))); bitmap.Freeze(); return bitmap; }
    private static UsageSnapshot DemoUsage()
    {
        // Deliberately synthetic values for public screenshots. No account data is loaded.
        using var document = System.Text.Json.JsonDocument.Parse("""{"rateLimitsByLimitId":{"codex":{"planType":"pro","primary":{"usedPercent":24,"windowDurationMins":10080,"resetsAt":1893499200},"credits":{"balance":"1234.56","unlimited":false}}}}""");
        return UsageSnapshot.Parse(document.RootElement, DateTimeOffset.UtcNow);
    }
    private void Cleanup()
    {
        _closing = true; SavePosition(); _lifetime.Cancel();
        FinishActivity(false);_motionTimer.Stop();_music.Close();_interactionPanel?.Close();
        _refreshTimer.Stop(); _clockTimer.Stop(); _idleTimer.Stop(); _bubbleTimer.Stop();
        _animation.Dispose();
        _tray.Visible = false; var icon = _tray.Icon; _tray.Dispose(); icon?.Dispose();
        if (_hotkeyRegistered) NativeMethods.UnregisterHotKey(_hwnd, 1);
        _source?.RemoveHook(WindowProc);
        SystemEvents.DisplaySettingsChanged -= DisplayChanged; SystemEvents.PowerModeChanged -= PowerChanged;
    }
}
