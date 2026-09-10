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
    private async Task TestFullInteractionsAsync(Action<bool,string> check,string output)
    {
        _idleTimer.Stop();_settings.AutoInteract=false;_settings.AutoMove=false;
        _settings.Compact=false;_settings.Scale=1;_settings.ShowQuotaBubble=true;ApplySize();ResetPosition();
        check(_catalog.Clips.Count==609&&_catalog.Clips.Sum(c=>c.Frames.Count)==6181,"complete pinned upstream inventory: 609 clips and 6181 frames");
        check(_catalog.Foods.Count==123&&_catalog.Activities.Count==13&&_catalog.Recipes.Count==12,"all food, activity and layered recipe definitions imported");
        int total=0;
        foreach(var clip in _args.Contains("--interaction-smoke")?Array.Empty<PetClip>():_catalog.Clips)
        {
            PlayExactClip(clip);_animation.Pause(true);
            check(PetImage.Source is BitmapSource {PixelWidth:>0},"decoded complete animation group: "+clip.Id);
            if(++total%30==0)
            {
                await File.WriteAllTextAsync(Path.Combine(output,"full-test-progress.txt"),$"Decoded {total}/609 groups");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
            }
        }
        StopPetActivity();_bubbleTimer.Stop();SpeechBubble.Visibility=Visibility.Collapsed;
        foreach(string mood in _args.Contains("--interaction-smoke")?Array.Empty<string>():new[]{"happy","nomal","poorcondition","ill"})
        {
            _settings.MoodOverride=mood;
            foreach(string family in _catalog.Families.Where(f=>f is not ("Eat" or "Drink" or "Gift")))
            {
                StartAction(family,2);_animation.Pause(true);
                check(_actionFamily==family&&_animation.Current.StartsWith('@'),$"interaction resolves: {family}/{mood}");
            }
        }
        _settings.MoodOverride="happy";StopPetActivity();
        double coins=_settings.Life.Coins;
        var food=_catalog.Foods.First(f=>f.Type=="Meal");
        FeedPet(food);check(_animation.Current=="food","feeding starts front/item/back composite");
        StopPetActivity();check(_settings.Life.Coins==coins,"cancelled feeding never consumes currency");
        FeedPet(food);
        var until=DateTimeOffset.UtcNow.AddSeconds(15);
        while(_actionFamily=="food"&&DateTimeOffset.UtcNow<until)await Task.Delay(100);
        check(_actionFamily!="food"&&Math.Abs(_settings.Life.Coins-(coins-food.Price))<.001,"completed feeding applies stats and price once");
        double paid=_settings.Life.Coins;TickPetLife();check(_settings.Life.Coins==paid,"feeding cannot be charged a second time by clock ticks");
        foreach(var recipe in _catalog.Recipes)
        {
            CancelInteraction();_animation.PlayFood(recipe,food.File,()=>{});_animation.Pause(true);
            check(PetImage.Source is BitmapSource {PixelWidth:>0},$"layered recipe rendered: {recipe.Id}/{recipe.Mood}");
        }
        StartActivity(_catalog.Activities.First(a=>a.Type=="Work"),1);
        _activityStart=DateTimeOffset.UtcNow.AddMinutes(-1);_activityEnd=DateTimeOffset.UtcNow.AddSeconds(-1);
        double before=_settings.Life.Coins;TickPetLife();double after=_settings.Life.Coins;TickPetLife();
        check(after>before&&_settings.Life.Coins==after&&_activity is null,"activity completion grants earned reward exactly once");
        StartActivity(_catalog.Activities.First(a=>a.Type=="Study"),1);StopPetActivity();check(_activity is null&&_actionFamily is null,"stop cancels activity and returns to idle");
        ResetPosition();Left=WorkingArea().Left+WorkingArea().Width/2;Top=WorkingArea().Top+WorkingArea().Height/2;
        MovePet("MOVE/walk.right");double initial=Left;
        await Task.Delay(400);check(Left>initial,"walking updates native window position");StopPetActivity();
        MovePet("MOVE/climb.right");double initialTop=Top;
        await Task.Delay(400);check(Top<initialTop,"wall climbing updates vertical position");StopPetActivity();
        StartAction("Raise/Raised_Static",0);_dragging=true;var currentCursor=System.Windows.Forms.Cursor.Position;_dragCursor=new System.Drawing.Point(currentCursor.X-100,currentCursor.Y);MotionTick();
        check(_actionFamily=="Raise/Raised_Dynamic","drag motion selects raised dynamic animation");_dragging=false;StopPetActivity();
        HideAtSide("SideHide_Right_Main");SidePeek(true);check(_actionFamily=="SideHide_Right_Rise","side hiding responds with peek animation");
        SidePeek(false);check(_actionFamily=="SideHide_Right_Main","pointer leaving returns to side hiding");StopPetActivity();
        HideAtSide("SideHide_Left_Main");FocusClick(this,new RoutedEventArgs());SidePeek(true);
        check(_sideHideFamily is null&&_focusEnd is not null&&_animation.Current=="focusIn","new focus activity clears side-hover callbacks");StopPetActivity();
        TouchAt(new Point(PetImage.Width*.35,PetImage.Height*.3));check(_actionFamily=="Pinch","cheek hit region invokes pinch");
        TouchAt(new Point(PetImage.Width*.5,PetImage.Height*.5));check(_actionFamily=="Touch_Body","body hit region invokes body touch");StopPetActivity();
        foreach(string state in new[]{"Touch_Body","MOVE/climb.right","Pinch","WORK/GrilledSausage","IDEL/Tennis"})
        {
            StartAction(state,0);var pose=_catalog.Choose(state,Mood,"B");if(pose is not null)_animation.Play("@"+pose.Id);_animation.Pause(true);UpdateLayout();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
            SaveVisual(Root,Path.Combine(output,"interaction-"+state.Replace('/','-')+".png"));
        }
        StopPetActivity();FeedPet(food);await Task.Delay(1600);_animation.Pause(true);UpdateLayout();SaveVisual(Root,Path.Combine(output,"interaction-feeding.png"));StopPetActivity();
        ShowInteractionPanel();_interactionPanel!.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);
        SaveVisual((FrameworkElement)_interactionPanel.Content,Path.Combine(output,"interaction-panel.png"));
        var panel=(DockPanel)_interactionPanel.Content;var tabs=panel.Children.OfType<TabControl>().Single();
        for(int index=1;index<4;index++){tabs.SelectedIndex=index;_interactionPanel.UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Render);SaveVisual(panel,Path.Combine(output,$"interaction-panel-{index}.png"));}
        check(tabs.Items.Count==4,"interaction panel exposes care, activities and complete searchable gallery");
        _interactionPanel.Close();StopPetActivity();
        await File.WriteAllTextAsync(Path.Combine(output,"full-test-progress.txt"),"Full interaction tests complete");
    }
    private static void SaveVisual(FrameworkElement visual,string path)
    {
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*2),(int)Math.Ceiling(visual.ActualHeight*2),192,192,PixelFormats.Pbgra32);bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);
    }
}
