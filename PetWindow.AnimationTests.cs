using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Momo;

public partial class PetWindow
{
    private async Task TestAnimationRepairsAsync(Action<bool, string> check, string directory)
    {
        _motionTimer.Stop(); _clockTimer.Stop(); _idleTimer.Stop();
        StopPetActivity(); _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed;
        check(FindName("ActionBar") is null && FindName("PatButton") is null && FindName("SleepButton") is null, "bottom action controls removed");
        ClickFocusMenu(); check(_focusEnd is not null, "right-click menu starts focus");
        var focusMenu = OpenMenu();
        check(focusMenu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, "结束专注")), "focus menu reflects running timer");
        check(!focusMenu.Items.OfType<MenuItem>().Any(i => i.Header is string s && (s.Contains("摸摸") || s.Contains("休息"))), "legacy pat/rest shortcuts absent from menu");
        focusMenu.IsOpen = false; ClickFocusMenu(); check(_focusEnd is null, "right-click menu ends focus");
        foreach (bool compact in new[] { false, true })
        foreach (double scale in new[] { .8, 1.0, 1.2 })
        foreach (string mood in new[] { "happy", "nomal", "poorcondition", "ill" })
        {
            _settings.Compact = compact; _settings.Scale = scale; _settings.MoodOverride = mood;
            _settings.ShowQuotaBubble = true;
            _dragging = true; BeginRaisedDrag();
            foreach (string family in new[] { "Raise/Raised_Static", "Raise/Raised_Dynamic" })
            {
                StartAction(family, 0, prepare: false); LoopAction(_actionGeneration);
                var target = new System.Drawing.Point(System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea.Right - 55, 180);
                BindRaisedToCursor(target);
                var actual = PetImage.PointToScreen(RaisedAnchor());
                check((actual - new Point(target.X, target.Y)).Length <= 1.5, $"shirt grip follows cursor: {family}, {mood}, compact={compact}, scale={scale}");
                var authored = _catalog.RaisePoint(mood);
                check(authored == (mood == "ill" ? new Point(225, 115) : new Point(290, 128)), $"official authored raise point: {mood}");
            }
            _dragging = false; StopPetActivity();
        }
        _dragging = true; BeginRaisedDrag();
        bool captured = PetImage.CaptureMouse(); PetImage.ReleaseMouseCapture();
        check(captured && !_dragging && _moveVelocity == default(Vector), "lost mouse capture releases raised drag safely");
        foreach (bool compact in new[] { false, true })
        foreach (double scale in new[] { .8, 1.0, 1.2 })
        foreach (bool bubble in new[] { true, false })
        foreach (string family in new[] { "MOVE/climb.right", "MOVE/climb.left", "MOVE/climb.top.right", "SideHide_Right_Main", "SideHide_Left_Main" })
        {
            _settings.Compact = compact; _settings.Scale = scale; _settings.ShowQuotaBubble = bubble; _settings.MoodOverride = "happy";
            PerformFamily(family); _moveVelocity = default; LoopAction(_actionGeneration); ClampPosition();
            var pet = PetOpaqueBounds(); var area = WorkingArea();
            double gap = RightWall ? area.Right - Left - pet.Right * WindowScale.ScaleX : LeftWall ? Left + pet.Left * WindowScale.ScaleX - area.Left : Top + pet.Top * WindowScale.ScaleY - area.Top;
            check(Math.Abs(gap) < 1, $"character attached to edge: {family}, compact={compact}, scale={scale}, bubble={bubble}, gap={gap:0.###}");
            if (bubble)
            {
                var q = QuotaBubble.TranslatePoint(new Point(), Root);
                var bubbleRect = new Rect(Left + q.X * WindowScale.ScaleX, Top + q.Y * WindowScale.ScaleY, 160 * WindowScale.ScaleX, 134 * WindowScale.ScaleY);
                var toleranceArea = area; toleranceArea.Inflate(1, 1);
                check(toleranceArea.Contains(bubbleRect), $"edge bubble fully visible: {family}, compact={compact}, scale={scale}");
                check(RightWall ? q.X + 170 <= PetStage.Margin.Left : LeftWall ? q.X - 10 >= PetStage.Margin.Left + PetStage.Width : q.Y - 10 >= PetStage.Margin.Top + PetStage.Height, $"bubble placed inward: {family}");
            }
        }
        _settings.Compact = false; _settings.Scale = 1; _settings.ShowQuotaBubble = true;
        foreach (string family in new[] { "MOVE/climb.right", "MOVE/climb.left", "MOVE/climb.top.right", "Raise/Raised_Dynamic", "Default" })
        {
            StartAction(family, 0);
            if (family != "Default") LoopAction(_actionGeneration);
            _animation.Pause(true);
            if (family == "Default") { Top = WorkingArea().Top + 250; ArrangePet(); }
            _bubbleTimer.Stop(); SpeechBubble.Visibility = Visibility.Collapsed; ClampPosition();
            // Let the compositor finish the native window resize before capturing the visual tree.
            await Task.Delay(100); UpdateLayout();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Root.Width * 2), (int)Math.Ceiling(Root.Height * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(Root);
            check(!AnimationPlayer.OpaqueBounds(bitmap).IsEmpty, $"adaptive layout produces visible pixels: {family}");
            if (family is "MOVE/climb.right" or "MOVE/climb.left")
            {
                var tip = QuotaTail.TranslatePoint(new Point(5, 8), Root);
                var pixel = new byte[4];
                bitmap.CopyPixels(new Int32Rect((int)(tip.X * 2), (int)(tip.Y * 2), 1, 1), pixel, 4, 0);
                check(pixel[3] > 240 && pixel[2] > 230, $"side bubble tail is not clipped: {family}");
            }
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory, "repair-" + family.Replace('/', '-') + ".png")); encoder.Save(stream);
        }
        StopPetActivity(); _settings.MoodOverride = "auto"; _motionTimer.Start(); _clockTimer.Start(); _idleTimer.Start();
    }
}
