using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Momo;

public partial class PetWindow
{
    private enum BubbleSide { Above, Left, Right, Below }
    private BubbleSide _bubbleSide;
    private bool _arranging, _boundsQueued;
    private bool RightWall => _actionFamily is "MOVE/climb.right" || _actionFamily?.StartsWith("SideHide_Right") == true;
    private bool LeftWall => _actionFamily is "MOVE/climb.left" || _actionFamily?.StartsWith("SideHide_Left") == true;
    private bool TopWall => _actionFamily?.StartsWith("MOVE/climb.top") == true;

    // Layout changes preserve the sprite's screen origin, including when the bubble is toggled.
    private void ArrangePet()
    {
        if (_arranging) return;
        _arranging = true;
        try
        {
            double oldX = Left + PetStage.Margin.Left * WindowScale.ScaleX;
            double oldY = Top + PetStage.Margin.Top * WindowScale.ScaleY;
            double size = _settings.Compact ? 192 : 256;
            var area = WorkingArea();
            var side = RightWall ? BubbleSide.Left : LeftWall ? BubbleSide.Right : TopWall ? BubbleSide.Below : BubbleSide.Above;
            if (side == BubbleSide.Above && double.IsFinite(oldY) && oldY - 132 * _settings.Scale < area.Top)
                side = BubbleSide.Below;
            _bubbleSide = side;
            bool column = side is BubbleSide.Left or BubbleSide.Right;
            bool hasControls = _settings.ShowQuotaBubble || SpeechBubble.Visibility == Visibility.Visible || FocusBadge.Visibility == Visibility.Visible;
            double gutter = column && hasControls ? 176 : 0;
            double above = side == BubbleSide.Above && _settings.ShowQuotaBubble ? 132 : 0;
            double petX = side == BubbleSide.Left ? gutter : 0, petY = 8 + above;
            PetStage.Width = PetStage.Height = PetImage.Width = PetImage.Height = size;
            PetStage.Margin = new Thickness(petX, petY, 0, 0);
            PetShadow.Visibility = _dragging || _actionFamily?.StartsWith("Raise/") == true || RightWall || LeftWall || TopWall || _actionFamily?.Contains("fall") == true ? Visibility.Collapsed : Visibility.Visible;
            QuotaBubble.Visibility = _settings.ShowQuotaBubble ? Visibility.Visible : Visibility.Collapsed;
            double qx = side == BubbleSide.Left ? 6 : side == BubbleSide.Right ? size + 10 : (size - 160) / 2;
            double qy = column ? 18 : side == BubbleSide.Below ? size + 18 : 6;
            QuotaBubble.Margin = new Thickness(qx, qy, 0, 0);
            // Tail geometry is independent of the information card, whose readable size stays fixed.
            QuotaTail.Data = Geometry.Parse(side switch
            {
                BubbleSide.Left => "M0,0 L10,8 L0,16",
                BubbleSide.Right => "M10,0 L0,8 L10,16",
                BubbleSide.Below => "M0,10 L8,0 L16,10",
                _ => "M0,0 L8,10 L16,0"
            });
            QuotaTail.Width = column ? 12 : 18; QuotaTail.Height = column ? 18 : 12;
            QuotaTail.Margin = side switch
            {
                BubbleSide.Left => new Thickness(159,40,0,0),
                BubbleSide.Right => new Thickness(-9,40,0,0),
                BubbleSide.Below => new Thickness(72,-9,0,0),
                _ => new Thickness(72,123,0,0)
            };
            SpeechBubble.HorizontalAlignment = FocusBadge.HorizontalAlignment = HorizontalAlignment.Left;
            double infoX = column ? qx : Math.Max(6, size - 160);
            double speechY = column ? 150 : side == BubbleSide.Below ? size + (_settings.ShowQuotaBubble ? 154 : 18) : petY + 134;
            double focusY = column ? 204 : side == BubbleSide.Below ? speechY + (SpeechBubble.Visibility == Visibility.Visible ? 55 : 0) : petY + Math.Max(174, size - 58);
            SpeechBubble.Margin = new Thickness(infoX, speechY, 0, 0);
            FocusBadge.Margin = new Thickness(infoX, focusY, 0, 0);
            Root.Width = size + gutter;
            Root.Height = petY + size + 8;
            if (_settings.ShowQuotaBubble) Root.Height = Math.Max(Root.Height, qy + 140);
            if (SpeechBubble.Visibility == Visibility.Visible) Root.Height = Math.Max(Root.Height, speechY + 55);
            if (FocusBadge.Visibility == Visibility.Visible) Root.Height = Math.Max(Root.Height, focusY + 54);
            double scale = Math.Min(_settings.Scale, Math.Min((area.Width - 20) / Root.Width, (area.Height - 20) / Root.Height));
            WindowScale.ScaleX = WindowScale.ScaleY = scale;
            Width = Root.Width * scale; Height = Root.Height * scale;
            if (double.IsFinite(oldX)) Left = oldX - petX * scale;
            if (double.IsFinite(oldY)) Top = oldY - petY * scale;
            UpdateLayout();
        }
        finally { _arranging = false; }
    }

    private Rect PetOpaqueBounds()
    {
        var pose = _animation.VisibleBounds;
        if (pose.IsEmpty) pose = new Rect(.5, .5, 0, 0);
        return new Rect(PetStage.Margin.Left + pose.Left * PetStage.Width,
            PetStage.Margin.Top + pose.Top * PetStage.Height, pose.Width * PetStage.Width, pose.Height * PetStage.Height);
    }

    private Rect VisibleHorizontalBounds()
    {
        var bounds = PetOpaqueBounds();
        foreach (var element in new FrameworkElement[] { PetShadow, QuotaBubble, SpeechBubble, FocusBadge })
        {
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0) continue;
            var origin = element.TranslatePoint(new Point(), Root);
            double shadow = element == QuotaBubble ? 10 : 0;
            bounds.Union(new Rect(origin.X - shadow, origin.Y - shadow, element.ActualWidth + shadow * 2, element.ActualHeight + shadow * 2));
        }
        bounds.Inflate(2, 2);
        return bounds;
    }

    private void ClampPosition()
    {
        ArrangePet();
        var area = WorkingArea(); var bounds = VisibleHorizontalBounds(); var pet = PetOpaqueBounds();
        double sx = WindowScale.ScaleX, sy = WindowScale.ScaleY;
        if (!double.IsFinite(Left)) Left = area.Right - Width;
        if (!double.IsFinite(Top)) Top = area.Bottom - Height;
        double minLeft = area.Left - bounds.Left * sx, maxLeft = area.Right - bounds.Right * sx;
        double minTop = area.Top - bounds.Top * sy, maxTop = area.Bottom - bounds.Bottom * sy;
        Left = Math.Clamp(Left, minLeft, Math.Max(minLeft, maxLeft));
        Top = Math.Clamp(Top, minTop, Math.Max(minTop, maxTop));
        // Wall attachment belongs to the character. The complete bubble column sits inward.
        if (RightWall) Left = area.Right - pet.Right * sx;
        if (LeftWall) Left = area.Left - pet.Left * sx;
        if (TopWall) Top = area.Top - pet.Top * sy;
    }

    private void QueueBoundsCheck()
    {
        if (!IsLoaded || _closing || _boundsQueued) return;
        _boundsQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _boundsQueued = false;
            if (_closing) return;
            if (_dragging) BindRaisedToCursor(System.Windows.Forms.Cursor.Position);
            else ClampPosition();
        }, DispatcherPriority.Loaded);
    }

    private Point RaisedAnchor()
    {
        // Use the rendered clip's mood, since missing variants can fall back to another mood.
        string mood = _animation.Current.StartsWith("@") && _catalog.ById.TryGetValue(_animation.Current[1..], out var clip) ? clip.Mood : Mood;
        var point = _catalog.RaisePoint(mood);
        return new Point(point.X / 500 * PetImage.ActualWidth, point.Y / 500 * PetImage.ActualHeight);
    }

    private void BindRaisedToCursor(System.Drawing.Point cursor)
    {
        ArrangePet();
        var anchor = PetImage.PointToScreen(RaisedAnchor());
        var dpi = VisualTreeHelper.GetDpi(this);
        Left += (cursor.X - anchor.X) / dpi.DpiScaleX;
        Top += (cursor.Y - anchor.Y) / dpi.DpiScaleY;
        // Do not clamp while held: the grip point must remain exactly under the mouse.
    }

    private void ClickFocusMenu()
    {
        var menu = OpenMenu();
        foreach (var entry in menu.Items)
            if (entry is MenuItem item && item.Header is string label && label is "专注 25 分钟" or "结束专注")
            { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); break; }
        menu.IsOpen = false;
    }
}
