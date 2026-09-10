using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Momo;

public sealed class AnimationPlayer : IDisposable
{
    private sealed record Frame(BitmapSource Image, int Duration);
    private readonly Dictionary<string, List<Frame>> _loaded = new();
    private readonly Dictionary<string, Rect> _bounds = new();
    private readonly JsonDocument _manifest;
    private readonly Image _image;
    private readonly DispatcherTimer _timer = new();
    private readonly string _assetRoot;
    private readonly VPetCatalog? _catalog;
    private List<Frame> _frames = new();
    private int _index;
    private bool _loop;
    private Action? _onCompleted;
    public string Current { get; private set; } = "";
    public Rect VisibleBounds => _bounds.TryGetValue(Current, out var bounds) ? bounds : new Rect(0, 0, 1, 1);
    public event Action? BoundsChanged;
    public AnimationPlayer(Image image, VPetCatalog? catalog = null)
    {
        _image = image;
        _catalog = catalog;
        _assetRoot = Path.Combine(AppContext.BaseDirectory, "assets");
        _manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_assetRoot, "animations.json")));
        _timer.Tick += (_, _) => Advance();
    }
    private List<Frame> Load(string name)
    {
        if (_loaded.TryGetValue(name, out var frames)) return frames;
        frames = new();
        var bounds = Rect.Empty;
        var sourceFrames = name.StartsWith('@') && _catalog is not null
            ? _catalog.ById[name[1..]].Frames
            : _manifest.RootElement.GetProperty("animations").GetProperty(name).EnumerateArray().Select(item => new PetFrame(item.GetProperty("file").GetString()!, item.GetProperty("duration").GetInt32(), "", "")).ToList();
        var decoded = new Dictionary<string, BitmapImage>();
        foreach (var item in sourceFrames)
        {
            if (!decoded.TryGetValue(item.File, out var bitmap))
            {
            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 384;
            bitmap.UriSource = new Uri(Path.Combine(_assetRoot, item.File));
            bitmap.EndInit(); bitmap.Freeze();
            decoded[item.File] = bitmap;
            bounds.Union(OpaqueBounds(bitmap));
            }
            frames.Add(new Frame(bitmap, Math.Max(1, item.Duration)));
        }
        if (frames.Count == 0) throw new InvalidDataException("No animation frames.");
        _loaded[name] = frames;
        _bounds[name] = bounds;
        // Keep a small working set; thousands of decoded full-pack frames must not accumulate.
        foreach (var key in _loaded.Keys.ToArray())
        {
            if (_loaded.Sum(x => x.Value.Count) <= 120) break;
            if (key != name && key != Current) _loaded.Remove(key);
        }
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
    public void PlayFood(FoodRecipe recipe, string? itemFile, Action completed)
    {
        var back = recipe.Back is not null ? Load("@" + recipe.Back) : new List<Frame>();
        var front = recipe.Front is not null ? Load("@" + recipe.Front) : new List<Frame>();
        BitmapImage? item = itemFile is null ? null : new BitmapImage(new Uri(Path.Combine(_assetRoot, itemFile)));
        var times = new SortedSet<int> { 0 };
        static int AddTimes(IEnumerable<int> durations, SortedSet<int> times) { int t=0;foreach(int duration in durations){t+=duration;times.Add(t);}return t; }
        int end = Math.Max(AddTimes(back.Select(f=>f.Duration),times), Math.Max(AddTimes(front.Select(f=>f.Duration),times),AddTimes(recipe.Positions.Select(p=>(int)p[0]),times)));
        var timeline=times.ToList(); var frames=new List<Frame>(); var bounds=Rect.Empty;
        static Frame? At(List<Frame> frames,int time) { foreach(var frame in frames){if(time<frame.Duration)return frame;time-=frame.Duration;}return frames.LastOrDefault(); }
        for(int i=0;i<timeline.Count-1;i++)
        {
            int t=timeline[i];var visual=new DrawingVisual();
            using(var dc=visual.RenderOpen())
            {
                if(At(back,t) is {} b)dc.DrawImage(b.Image,new Rect(0,0,384,384));
                int pt=t;double[]? position=null;foreach(var p in recipe.Positions){if(pt<p[0]){position=p;break;}pt-=(int)p[0];}
                if(item is not null && position is {Length:>=4})
                {
                    double factor=384d/500,x=position[1]*factor,y=position[2]*factor,w=position[3]*factor;
                    dc.PushOpacity(position.Length>5?position[5]:1);
                    dc.PushTransform(new RotateTransform(position.Length>4?position[4]:0,x+w/2,y+w/2));
                    dc.DrawImage(item,new Rect(x,y,w,w));dc.Pop();dc.Pop();
                }
                if(At(front,t) is {} f)dc.DrawImage(f.Image,new Rect(0,0,384,384));
            }
            var bitmap=new RenderTargetBitmap(384,384,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);bitmap.Freeze();bounds.Union(OpaqueBounds(bitmap));
            frames.Add(new Frame(bitmap,timeline[i+1]-t));
        }
        _timer.Stop();_frames=frames;_index=0;Current="food";_bounds[Current]=bounds;_loop=false;_onCompleted=completed;ShowFrame();_timer.Start();BoundsChanged?.Invoke();
    }
    public void Stop() { _timer.Stop(); _onCompleted=null; }
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
