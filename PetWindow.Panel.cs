using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Momo;

public partial class PetWindow
{
    private void ShowInteractionPanel()
    {
        if(_interactionPanel is not null){_interactionPanel.Show();_interactionPanel.Activate();return;}
        var window=new Window {Title="Momo · 互动与动画图鉴",Width=740,Height=650,MinWidth=580,MinHeight=450,Background=Brush("#FFFCFF"),WindowStartupLocation=WindowStartupLocation.CenterScreen};
        _interactionPanel=window;
        var shell=new DockPanel {Margin=new Thickness(20),Background=Brush("#FFFCFF")};
        var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
        header.Children.Add(new TextBlock {Text="和 Momo 一起生活",FontSize=22,FontWeight=FontWeights.SemiBold,Foreground=Brush("#775696")});
        var stats=new TextBlock {Margin=new Thickness(0,8,0,6),TextWrapping=TextWrapping.Wrap,FontSize=12};header.Children.Add(stats);
        var choices=new WrapPanel {Margin=new Thickness(0,0,0,12)};header.Children.Add(choices);
        var mood=new ComboBox {Width=108,Margin=new Thickness(0,0,10,0)};
        string[] moods={"auto","happy","nomal","poorcondition","ill"};
        foreach(var m in moods)mood.Items.Add(m=="auto"?"状态：自动":VPetCatalog.MoodLabel(m));
        mood.SelectedIndex=Array.IndexOf(moods,_settings.MoodOverride);
        mood.SelectionChanged+=(_,_)=>{_settings.MoodOverride=moods[Math.Max(0,mood.SelectedIndex)];_settings.Save();StopPetActivity();};choices.Children.Add(mood);
        void Check(string title,bool value,Action<bool> change){var c=new CheckBox {Content=title,IsChecked=value,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};c.Click+=(_,_)=>{change(c.IsChecked==true);_settings.Save();_refreshInteractionStats?.Invoke();};choices.Children.Add(c);}
        Check("自主互动",_settings.AutoInteract,v=>_settings.AutoInteract=v);
        Check("自主走动",_settings.AutoMove,v=>_settings.AutoMove=v);
        Check("自动购买补充（<60）",_settings.AutoCareEnabled,v=>_settings.AutoCareEnabled=v);
        choices.Children.Add(PanelButton("停止当前动作",StopPetActivity));
        var autoCareStatus=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=Brush("#8F839D"),Margin=new Thickness(0,0,0,10)};
        header.Children.Add(autoCareStatus);
        var tabs=new TabControl();shell.Children.Add(tabs);
        void Tab(string name,UIElement content)=>tabs.Items.Add(new TabItem {Header=name,Content=content,Padding=new Thickness(12,7,12,7)});
        var actions=new StackPanel {Margin=new Thickness(12)};
        actions.Children.Add(new TextBlock {Text="点击角色的头、身体或脸颊进行互动；按住拖动会提起角色。\n新动作会结束当前动作。活动收益按实际进行时间结算。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)});
        var groups=new[] {("亲密互动",new[]{"Touch_Head","Touch_Body","Pinch","Raise/Raised_Static","Raise/Raised_Dynamic","Think","Sleep"}),
            ("玩耍与休闲",_catalog.Families.Where(f=>f.StartsWith("IDEL/")||f.StartsWith("State/")).ToArray()),
            ("移动与边缘",_catalog.Families.Where(f=>f.StartsWith("MOVE/")||f.StartsWith("SideHide_")).ToArray()),
            ("表情与庆祝",new[]{"Say/Self","Say/Serious","Say/Shining","Say/Shy","Music","BDay","LevelUP","StartUP","Shutdown","Switch/Up","Switch/Down","Switch/Hunger","Switch/Thirsty"})};
        foreach(var (title,families) in groups)
        {
            actions.Children.Add(new TextBlock {Text=title,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,12,0,6)});
            var wrap=new WrapPanel();actions.Children.Add(wrap);
            foreach(string family in families)wrap.Children.Add(PanelButton(VPetCatalog.Label(family),()=>PerformFamily(family)));
        }
        var talk=new DockPanel {Margin=new Thickness(0,16,0,0)};
        var text=new TextBox {Text="今天也一起加油吧 ♡",MaxLength=120,MinWidth=250,Margin=new Thickness(0,0,8,0)};
        var talkButton=PanelButton("说出来",()=>{StartAction("Say/Shining",7);Say(text.Text,false);});DockPanel.SetDock(talkButton,Dock.Right);talk.Children.Add(talkButton);talk.Children.Add(text);actions.Children.Add(talk);
        Tab("互动",new ScrollViewer {Content=actions,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        Tab("投喂与礼物",BuildFoodPanel());
        Tab("工作与娱乐",BuildActivityPanel());
        Tab("动画图鉴",BuildGalleryPanel());
        window.Content=shell;
        void RefreshStats(){var l=_settings.Life;stats.Text=$"Lv.{l.Level}  ·  宠物金币 {l.Coins:0.0}  ·  经验 {l.Experience:0}  ·  好感 {l.Affection:0}\n体力 {l.Energy:0}   饱腹 {l.Hunger:0}   水分 {l.Thirst:0}   心情 {l.Feeling:0}   健康 {l.Health:0}   ·  {VPetCatalog.MoodLabel(Mood)}\n{(_activity is {} a?$"正在{a.Name}":_actionFamily is {} f?$"正在{VPetCatalog.Label(f)}":_sleeping?"正在休息":"自由活动")}  ·  宠物金币独立于 Codex credits";}
        void RefreshAutoCare(){autoCareStatus.Text=$"自动补充：{(_settings.AutoCareEnabled?"已开启":"已关闭")} · 体力、饱腹、水分、心情、健康低于 60 时购买补充，每 30 秒最多一件。\n{_lastAutoCare}";}
        _refreshInteractionStats=()=>{RefreshStats();RefreshAutoCare();};
        var refresh=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};refresh.Tick+=(_,_)=>_refreshInteractionStats?.Invoke();refresh.Start();_refreshInteractionStats();
        window.Closed+=(_,_)=>{refresh.Stop();_refreshInteractionStats=null;_interactionPanel=null;};window.Show();
    }
    private static Button PanelButton(string title,Action action)
    {var b=new Button {Content=title,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,6,6),MinHeight=30};b.Click+=(_,_)=>action();return b;}
    private UIElement BuildFoodPanel()
    {
        var root=new DockPanel {Margin=new Thickness(12)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock {Text=$"{_catalog.Foods.Count} 种官方物品 · 动画完成后扣除宠物金币并生效，中途停止不扣费。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        top.Children.Add(new TextBlock {Text="自动补充直接扣除宠物金币并生效，优先买能补到 60 的最便宜道具；不够补到 60 时选择恢复量／金币更划算的道具。跳过有负面指标效果的道具，金币不足则等待。工作与专注可继续，手动投喂时暂停自动购买。",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,0,0,8)});
        var search=new TextBox {Margin=new Thickness(0,0,0,8),ToolTip="按名称、类型或介绍搜索"};top.Children.Add(search);
        var bottom=new StackPanel {Margin=new Thickness(0,8,0,0)};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var detail=new TextBlock {TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)};bottom.Children.Add(detail);
        var buttons=new WrapPanel();bottom.Children.Add(buttons);
        var list=new ListBox();root.Children.Add(list);
        void Refresh(){list.Items.Clear();foreach(var food in _catalog.Foods.Where(f=>$"{f.Name} {f.Type} {f.Description}".Contains(search.Text,StringComparison.OrdinalIgnoreCase)))list.Items.Add(new ListBoxItem {Content=$"{food.Name}  ·  {food.Price:0.##} 金币  ·  {FoodType(food.Type)}",Tag=food,Padding=new Thickness(6)});}
        list.SelectionChanged+=(_,_)=>{if((list.SelectedItem as ListBoxItem)?.Tag is PetFood food)detail.Text=$"{food.Description}\n体力 {food.Energy:+0.#;-0.#;0}　饱腹 {food.Hunger:+0.#;-0.#;0}　水分 {food.Thirst:+0.#;-0.#;0}　心情 {food.Feeling:+0.#;-0.#;0}　健康 {food.Health:+0.#;-0.#;0}　经验 +{food.Exp:0.#}";};
        buttons.Children.Add(PanelButton("投喂选中物品",()=>{if((list.SelectedItem as ListBoxItem)?.Tag is PetFood food)FeedPet(food);}));
        buttons.Children.Add(PanelButton("免费喝水",()=>FeedPet(new PetFood("清水","Drink","",_catalog.Foods.FirstOrDefault(f=>f.Name.Contains("水")&&f.File is not null)?.File,"drink",0,2,0,25,1,1,0))));
        buttons.Children.Add(PanelButton("免费基础餐",()=>FeedPet(new PetFood("基础餐","Food","",_catalog.Foods.FirstOrDefault(f=>f.Name=="面包")?.File,"eat",0,10,25,0,2,1,0))));
        search.TextChanged+=(_,_)=>Refresh();Refresh();return root;
    }
    private static string FoodType(string type)=>type.ToLowerInvariant() switch {"drink"=>"饮料","meal"=>"正餐","snack"=>"零食","functional"=>"功能食品","food"=>"食物","drug"=>"药品","gift"=>"礼物",_=>type};
    private UIElement BuildActivityPanel()
    {
        var stack=new StackPanel {Margin=new Thickness(12)};
        stack.Children.Add(new TextBlock {Text="工作获得宠物金币，学习获得经验，娱乐改善心情。计时会持续，收益按实际时长结算；体力或健康过低会暂停。所有活动均可直接体验。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)});
        foreach(var activity in _catalog.Activities)
        {
            var row=new DockPanel {Margin=new Thickness(0,0,0,8)};
            var buttons=new WrapPanel();DockPanel.SetDock(buttons,Dock.Right);row.Children.Add(buttons);
            buttons.Children.Add(PanelButton("体验 1 分钟",()=>StartActivity(activity,1)));
            buttons.Children.Add(PanelButton("开始",()=>StartActivity(activity)));
            row.Children.Add(new TextBlock {Text=$"{activity.Name} · {activity.Minutes:0} 分钟\n{(activity.Type=="Work"?"工作":activity.Type=="Study"?"学习":"娱乐")} · 消耗饱腹与水分",VerticalAlignment=VerticalAlignment.Center});stack.Children.Add(row);
        }
        stack.Children.Add(PanelButton("结束活动并结算",StopPetActivity));
        return new ScrollViewer {Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    private UIElement BuildGalleryPanel()
    {
        var root=new DockPanel {Margin=new Thickness(12)};
        var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(new TextBlock {Text=$"完整目录：{_catalog.Clips.Count} 组 / {_catalog.Clips.Sum(c=>c.Frames.Count):N0} 帧。投喂的前后层可分别查看，实际投喂时自动合成。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
        var search=new TextBox {ToolTip="搜索动作、状态或源目录",Margin=new Thickness(0,0,0,8)};top.Children.Add(search);
        var footer=new WrapPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        var list=new ListBox();root.Children.Add(list);
        void Refresh(){list.Items.Clear();foreach(var clip in _catalog.Clips.Where(c=>$"{VPetCatalog.Label(c.Family)} {VPetCatalog.MoodLabel(c.Mood)} {c.Id}".Contains(search.Text,StringComparison.OrdinalIgnoreCase)))list.Items.Add(new ListBoxItem {Content=$"{VPetCatalog.Label(clip.Family)} · {VPetCatalog.MoodLabel(clip.Mood)} · {clip.Phase} · {clip.Frames.Count} 帧\n{clip.Id}",Tag=clip,Padding=new Thickness(6)});}
        void Play(){if((list.SelectedItem as ListBoxItem)?.Tag is PetClip clip)PlayExactClip(clip);}
        footer.Children.Add(PanelButton("播放选中动画",Play));footer.Children.Add(PanelButton("停止",StopPetActivity));
        list.MouseDoubleClick+=(_,_)=>Play();search.TextChanged+=(_,_)=>Refresh();Refresh();return root;
    }
}
