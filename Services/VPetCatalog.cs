using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Momo;

public sealed record PetFrame(string File, int Duration, string SourcePath, string GitBlob);
public sealed record PetClip(string Id, string Family, string Mood, string Phase, bool Layer, List<PetFrame> Frames);
public sealed record FoodRecipe(string Id, string Mood, string? Back, string? Front, List<double[]> Positions);
public sealed record PetFood(string Name, string Type, string Description, string? File, string Graph, double Price, double Energy, double Hunger, double Thirst, double Health, double Feeling, double Exp, double Affection = 1);
public sealed record PetActivity(string Name, string Type, string Family, double Minutes, double Reward, double HungerCost, double ThirstCost, double FeelingCost, double Bonus, int Level);

public sealed class VPetCatalog
{
    public IReadOnlyList<PetClip> Clips { get; }
    public IReadOnlyList<FoodRecipe> Recipes { get; }
    public IReadOnlyList<PetFood> Foods { get; }
    public IReadOnlyList<PetActivity> Activities { get; }
    public IReadOnlyDictionary<string, PetClip> ById { get; }
    private readonly Dictionary<string, System.Windows.Point> _raisePoints = new();
    public System.Windows.Point RaisePoint(string mood) => _raisePoints.TryGetValue(mood, out var point) ? point : _raisePoints["nomal"];
    public IEnumerable<string> Families => Clips.Where(c => !c.Layer).Select(c => c.Family).Distinct().OrderBy(Label);
    public VPetCatalog()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/vpet-catalog.json")));
        var root = doc.RootElement;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Clips = root.GetProperty("clips").Deserialize<List<PetClip>>(options)!;
        Recipes = root.GetProperty("recipes").Deserialize<List<FoodRecipe>>(options)!;
        ById = Clips.ToDictionary(c => c.Id);
        static string S(JsonElement e, string key, string fallback = "") => e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : fallback;
        static double N(JsonElement e, string key, double fallback = 0) => double.TryParse(S(e, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : fallback;
        var raise = root.GetProperty("touch").GetProperty("raisepoint");
        foreach (string mood in new[] { "happy", "nomal", "poorcondition", "ill" })
            _raisePoints[mood] = new System.Windows.Point(N(raise, mood + "_x"), N(raise, mood + "_y"));
        Foods = root.GetProperty("foods").EnumerateArray().Select(e => new PetFood(S(e,"name"), S(e,"type"), S(e,"desc"), S(e,"file") is { Length: > 0 } file ? file : null, S(e,"graph","eat"), N(e,"price"), N(e,"strength"), N(e,"strengthfood"), N(e,"strengthdrink"), N(e,"health"), N(e,"feeling"), N(e,"exp"), N(e,"likability",1))).ToList();
        Activities = root.GetProperty("activities").EnumerateArray().Select(e => new PetActivity(S(e,"name"), S(e,"type"), Clips.First(c => c.Family.StartsWith("WORK/") && c.Family[5..].Equals(S(e,"graph"), StringComparison.OrdinalIgnoreCase)).Family, N(e,"time",30), N(e,"moneybase",10), N(e,"strengthfood"), N(e,"strengthdrink"), N(e,"feeling"), N(e,"finishbonus"), (int)N(e,"levellimit",1))).ToList();
    }
    public PetClip? Choose(string family, string mood, string phase, string? style = null, bool random = true)
    {
        var familyClips = Clips.Where(c => c.Family == family && !c.Layer).ToList();
        var variants = familyClips.Where(c => c.Phase == phase).ToList();
        if (variants.Count == 0) return null;
        var matching = variants.Where(c => c.Mood == mood).ToList();
        if (matching.Count == 0) matching = variants.Where(c => c.Mood == "nomal").ToList();
        if (matching.Count == 0) matching = variants.Where(c => c.Mood == "happy").ToList();
        if (matching.Count == 0) matching = variants;
        if (style is not null && family == "Touch_Body")
        {
            var same = matching.Where(c => c.Id.Contains("Happy_Turn") == style.Contains("Happy_Turn")).ToList();
            if (same.Count > 0) matching = same;
        }
        return matching[random ? Random.Shared.Next(matching.Count) : 0];
    }
    public FoodRecipe Recipe(string graph, string mood)
    {
        string kind = graph.StartsWith("drink", StringComparison.OrdinalIgnoreCase) ? "drink" : graph.StartsWith("gift", StringComparison.OrdinalIgnoreCase) ? "gift" : "eat";
        return Recipes.FirstOrDefault(r => r.Id.StartsWith(kind, StringComparison.OrdinalIgnoreCase) && r.Mood == mood)
            ?? Recipes.First(r => r.Id.StartsWith(kind, StringComparison.OrdinalIgnoreCase));
    }
    public static string MoodLabel(string mood) => mood switch { "happy" => "开心", "nomal" => "平常", "poorcondition" => "低落", "ill" => "生病", _ => "自动" };
    public static string Label(string family) => Names.TryGetValue(family, out var name) ? name : family;
    private static readonly Dictionary<string, string> Names = new()
    {
        ["Default"]="自然待机",["Touch_Head"]="摸头",["Touch_Body"]="摸身体",["Pinch"]="捏脸",["Sleep"]="睡觉",["Think"]="思考",["Music"]="随音乐摇摆",["BDay"]="生日庆祝",["LevelUP"]="升级庆祝",["StartUP"]="迎接",["Shutdown"]="告别",["Eat"]="吃东西",["Drink"]="喝饮料",["Gift"]="收礼物",
        ["Raise/Raised_Static"]="提起 · 停留",["Raise/Raised_Dynamic"]="提起 · 摇晃",["State/StateONE"]="休闲姿势一",["State/StateTWO"]="休闲姿势二",
        ["IDEL/Boring"]="无聊发呆",["IDEL/Bubbles"]="吹泡泡",["IDEL/Meow"]="卖萌",["IDEL/Squat"]="蹲下",["IDEL/Tennis"]="打网球",["IDEL/amusement_B"]="自娱自乐",["IDEL/aside"]="侧身张望",["IDEL/happy_like520"]="比心",["IDEL/meowlook"]="猫猫张望",["IDEL/yawning"]="打哈欠",
        ["Say/Self"]="自信说话",["Say/Serious"]="认真说话",["Say/Shining"]="开心说话",["Say/Shy"]="害羞说话",
        ["Switch/Up"]="精神好起来",["Switch/Down"]="情绪低落",["Switch/Hunger"]="肚子饿了",["Switch/Thirsty"]="口渴了",
        ["SideHide_Left_Main"]="左侧躲藏",["SideHide_Left_Rise"]="左侧探头",["SideHide_Right_Main"]="右侧躲藏",["SideHide_Right_Rise"]="右侧探头",
        ["MOVE/climb.left"]="爬左墙",["MOVE/climb.right"]="爬右墙",["MOVE/climb.top.left"]="沿顶部向左爬",["MOVE/climb.top.right"]="沿顶部向右爬",["MOVE/crawl.left"]="向左爬行",["MOVE/crawl.right"]="向右爬行",["MOVE/fall.left"]="向左落下",["MOVE/fall.right"]="向右落下",["MOVE/walk.left"]="向左散步",["MOVE/walk.right"]="向右散步",["MOVE/walk.left.faster"]="向左快走",["MOVE/walk.right.faster"]="向右快走",["MOVE/walk.left.slow"]="向左慢走",["MOVE/walk.right.slow"]="向右慢走",
        ["WORK/Calligraphy"]="学书法",["WORK/FixMenu"]="修屏幕",["WORK/GrilledSausage"]="烧烤",["WORK/PlayONE"]="玩游戏",["WORK/PlayWater"]="玩水",["WORK/RemoveObject"]="删错误",["WORK/RopeSkipping"]="跳绳",["WORK/Study"]="学习",["WORK/StudyPaint"]="学画画",["WORK/StudyTWO"]="研究",["WORK/WorkClean"]="清屏",["WORK/WorkONE"]="文案",["WORK/WorkTWO"]="直播"
    };
}
