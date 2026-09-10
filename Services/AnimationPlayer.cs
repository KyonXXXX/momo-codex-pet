using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Momo;

public sealed class AnimationPlayer : IDisposable
{
    private sealed record Frame(BitmapImage Image, int Duration);
    private readonly Dictionary<string, List<Frame>> _loaded = new();
    private readonly Dictionary<string, Rect> _bounds = new();
    private readonly JsonDocument _manifest;
    private readonly Image _image;
    private readonly DispatcherTimer _timer = new();
    private readonly string _assetRoot;
    private List<Frame> _frames = new();
    private int _index;
    private bool _loop;
    private Action? _onCompleted;
    public string Current { get; private set; } = "";
    public Rect VisibleBounds => _bounds.TryGetValue(Current, out var bounds) ? bounds : new Rect(0, 0, 1, 1);
    public event Action? BoundsChanged;
    public AnimationPlayer(Image image)
    {
        _image = image;
        _assetRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        _manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_assetRoot, "animations.json")));
        _timer.Tick += (_, _) => Advance();
    }
    private List<Frame> Load(string name)
    {
        if (_loaded.TryGetValue(name, out var frames)) return frames;
        frames = new();
        var bounds = Rect.Empty;
        foreach (var item in _manifest.RootElement.GetProperty("animations").GetProperty(name).EnumerateArray())
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 520;
            bitmap.UriSource = new Uri(Path.Combine(_assetRoot, item.GetProperty("file").GetString()!));
            bitmap.EndInit(); bitmap.Freeze();
            bounds.Union(OpaqueBounds(bitmap));
            frames.Add(new Frame(bitmap, Math.Clamp(item.GetProperty("duration").GetInt32(), 40, 5000)));
        }
        if (frames.Count == 0) throw new InvalidDataException("No animation frames.");
        _loaded[name] = frames;
        _bounds[name] = bounds;
        return frames;
    }
    internal static Rect OpaqueBounds(BitmapSource bitmap)
    {
        var rgba = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = rgba.PixelWidth, height = rgba.PixelHeight;
        var pixels = new byte[width * height * 4];
        rgba.CopyPixels(pixels, width * 4, 0);
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (pixels[(y * width + x) * 4 + 3] != 0)
                { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        return right < 0 ? Rect.Empty : new Rect((double)left / width, (double)top / height, (double)(right - left + 1) / width, (double)(bottom - top + 1) / height);
    }
    public void Play(string name, bool loop = true, Action? onCompleted = null)
    {
        _timer.Stop(); _frames = Load(name); _index = 0; Current = name; _loop = loop; _onCompleted = onCompleted;
        ShowFrame(); _timer.Start();
        BoundsChanged?.Invoke();
    }
    private void Advance()
    {
        _index++;
        if (_index >= _frames.Count)
        {
            if (_loop) _index = 0;
            else { _timer.Stop(); var callback = _onCompleted; _onCompleted = null; callback?.Invoke(); return; }
        }
        ShowFrame();
    }
    private void ShowFrame() { _image.Source = _frames[_index].Image; _timer.Interval = TimeSpan.FromMilliseconds(_frames[_index].Duration); }
    public void Pause(bool paused) { if (paused) _timer.Stop(); else if (_frames.Count > 0) _timer.Start(); }
    public void Dispose() { _timer.Stop(); _manifest.Dispose(); _loaded.Clear(); _bounds.Clear(); }
}
