using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Momo;

public partial class PetWindow
{
    private readonly VPetCatalog _catalog = new();
    private readonly DispatcherTimer _motionTimer = new() { Interval=TimeSpan.FromMilliseconds(40) };
    private readonly MediaPlayer _music = new();
    private Window? _interactionPanel;
    private string? _actionFamily, _actionStyle;
    private string? _sideHideFamily;
    private DateTimeOffset? _actionUntil;
    private Action? _actionDone;
    private int _actionGeneration;
    private bool _actionEnding, _musicLoop;
    private Vector _moveVelocity;
    private DateTimeOffset _motionAt=DateTimeOffset.UtcNow, _lifeAt=DateTimeOffset.UtcNow;
    private DateTimeOffset _lastLifeSave=DateTimeOffset.UtcNow;
    private PetActivity? _activity;
    private DateTimeOffset _activityStart, _activityEnd;
    private int _lastLevel;
    private string _lastMood="happy";
    private System.Drawing.Point _dragCursor;
    private string Mood => _settings.MoodOverride=="auto"?_settings.Life.Mood:_settings.MoodOverride;
    private void InitializeInteractions()
    {
        _lastLevel=_settings.Life.Level;_lastMood=Mood;
        _motionTimer.Tick+=(_,_)=>MotionTick();_motionTimer.Start();
        PetImage.MouseEnter+=(_,_)=>SidePeek(true);
        PetImage.MouseLeave+=(_,_)=>SidePeek(false);
        _music.MediaEnded+=(_,_)=>{if(_musicLoop){_music.Position=TimeSpan.Zero;_music.Play();}};
        _music.MediaFailed+=(_,_)=>{if(!_musicLoop)return;_musicLoop=false;_music.Close();Say("无法播放这个音乐文件。");EndAction();};
    }
    private void CancelInteraction(bool settleActivity=true)
    {
        _sideHideFamily=null;
        if(settleActivity)FinishActivity(false);
        _actionGeneration++;_actionFamily=null;_actionDone=null;_actionUntil=null;_actionEnding=false;_moveVelocity=default;
        _musicLoop=false;_music.Stop();_animation.Stop();
    }
    private void PrepareInteraction()
    {
        _exitRequested=false;
        _sideHideFamily=null;
        CancelInteraction();
        _bubbleTimer.Stop();SpeechBubble.Visibility=Visibility.Collapsed;
        _sleeping=false;SleepButton.Content="☾ 休息";
        _focusEnd=null;FocusButton.Content="◷ 专注";FocusBadge.Visibility=Visibility.Collapsed;
    }
    private void StartAction(string family,double seconds=8,Action? completed=null,bool prepare=true)
    {
        if(prepare)PrepareInteraction();else { _actionGeneration++;_animation.Stop(); }
        _actionFamily=family;_actionStyle=null;_actionDone=completed;_actionEnding=false;
        _actionUntil=seconds>0?DateTimeOffset.UtcNow.AddSeconds(seconds):null;
        int generation=_actionGeneration;
        var first=_catalog.Choose(family,Mood,"A")??_catalog.Choose(family,Mood,"B")??_catalog.Choose(family,Mood,"Single");
        if(first is null){_actionFamily=null;ReturnToState();return;}
        _actionStyle=first.Id;
        if(first.Phase=="Single"&&seconds>0)_actionUntil=null;
        _animation.Play("@"+first.Id,false,()=>{if(generation==_actionGeneration)LoopAction(generation);});
    }
    private void LoopAction(int generation)
    {
        if(generation!=_actionGeneration||_actionFamily is null)return;
        if(_actionEnding||_actionUntil is {} until&&DateTimeOffset.UtcNow>=until){EndAction();return;}
        var loop=_catalog.Choose(_actionFamily,Mood,"B",_actionStyle);
        if(loop is null&&_actionUntil is null&&_actionFamily.StartsWith("Raise/"))loop=_catalog.Choose(_actionFamily,Mood,"Single",_actionStyle);
        if(loop is null){EndAction();return;}
        _animation.Play("@"+loop.Id,false,()=>LoopAction(generation));
    }
    private void EndAction()
    {
        if(_actionFamily is null||_actionEnding)return;
        _actionEnding=true;_moveVelocity=default;
        int generation=_actionGeneration;
        void Finish()
        {
            if(generation!=_actionGeneration)return;
            var done=_actionDone;_actionDone=null;_actionFamily=null;_actionUntil=null;_actionEnding=false;
            _musicLoop=false;_music.Stop();
            if(done is not null)done();else ReturnToState();
        }
        var end=_catalog.Choose(_actionFamily,Mood,"C",_actionStyle);
        if(end is null)Finish();else _animation.Play("@"+end.Id,false,Finish);
    }
    private void StopPetActivity()
    {
        _sideHideFamily=null;
        CancelInteraction();_sleeping=false;_focusEnd=null;
        SleepButton.Content="☾ 休息";FocusButton.Content="◷ 专注";FocusBadge.Visibility=Visibility.Collapsed;
        ReturnToState();Say("好啦，歇一会儿。");
    }
    private void TickPetLife()
    {
        var now=DateTimeOffset.UtcNow;
        if(!App.IsTestMode)_settings.Life.Tick((now-_lifeAt).TotalSeconds,_sleeping,_activity);
        _lifeAt=now;
        if(_actionUntil is {} until&&now>=until)EndAction();
        if(_activity is not null)
        {
            var left=_activityEnd-now;
            FocusTimeText.Text=$"{Math.Max(0,(int)left.TotalMinutes):00}:{Math.Max(0,left.Seconds):00}";
            if(left<=TimeSpan.Zero){FinishActivity(true);EndAction();}
            else if(_settings.Life.Energy<5||_settings.Life.Health<15){FinishActivity(false);EndAction();Say("有点累了，先吃点东西休息吧。");}
        }
        if(now-_lastLifeSave>TimeSpan.FromSeconds(30)){_settings.Save();_lastLifeSave=now;}
        if(_settings.Life.Level>_lastLevel&&_actionFamily is null&&_focusEnd is null&&!_sleeping)
        { _lastLevel=_settings.Life.Level;StartAction("LevelUP",3);Say($"升到 {_lastLevel} 级啦！"); }
        if(Mood!=_lastMood&&_actionFamily is null&&_focusEnd is null&&!_sleeping)
        { string old=_lastMood;_lastMood=Mood;StartAction(Mood=="happy"||old=="ill"?"Switch/Up":"Switch/Down",3); }
    }
    private void PetIdleAction()
    {
        if(!_settings.AutoInteract||_sleeping||_actionFamily is not null||_focusEnd is not null||_dragging||!IsVisible)return;
        if(_settings.AutoMove&&Random.Shared.Next(3)==0){MovePet(Random.Shared.Next(2)==0?"MOVE/walk.left":"MOVE/walk.right");return;}
        if(_settings.Life.Hunger<20){StartAction("Switch/Hunger",4);Say("肚子有点饿了。",false);return;}
        if(_settings.Life.Thirst<20){StartAction("Switch/Thirsty",4);Say("想喝点水。",false);return;}
        var families=_catalog.Families.Where(f=>f.StartsWith("IDEL/")||f.StartsWith("State/")).ToArray();
        StartAction(families[Random.Shared.Next(families.Length)],10);
    }
    private void PerformFamily(string family)
    {
        if(family.StartsWith("MOVE/")){MovePet(family);return;}
        if(family.StartsWith("SideHide_")){HideAtSide(family);return;}
        if(family=="Music"){PlayMusic();return;}
        if(family=="Sleep"){if(!_sleeping)SleepClick(this,new RoutedEventArgs());return;}
        if(family.StartsWith("WORK/")){StartActivity(_catalog.Activities.First(a=>a.Family==family));return;}
        if(family is "Eat" or "Drink" or "Gift") { ShowInteractionPanel();return; }
        if(family=="BDay"){CelebrateBirthday();return;}
        StartAction(family,family.StartsWith("Raise/")?5:8);
        if(family is "Touch_Head" or "Touch_Body") {_settings.Life.Feeling+=2;_settings.Life.Affection+=.2;_settings.Life.Normalize();}
        if(family.StartsWith("Say/"))Say(family switch {"Say/Shy"=>"有你在，真好呀。","Say/Serious"=>"一步一步，我们把它做好。","Say/Self"=>"今天也要元气满满！",_=>"一起加油吧 ♡"},false);
    }
    private void TouchAt(Point point)
    {
        double x=point.X/PetImage.ActualWidth*500,y=point.Y/PetImage.ActualHeight*500;
        string family=x>=149&&x<=205&&y>=128&&y<=187?"Pinch":x>=166&&x<=329&&y>=206&&y<=342?"Touch_Body":"Touch_Head";
        PerformFamily(family);Say(family=="Pinch"?"脸颊软软的～":"诶嘿，摸摸 ♡",false);
    }
    private void BeginRaisedDrag()
    {
        StartAction("Raise/Raised_Static",0);_dragCursor=System.Windows.Forms.Cursor.Position;
    }
    private void EndRaisedDrag(){_moveVelocity=default;EndAction();}
    private void MovePet(string family)
    {
        StartAction(family,8);UpdateLayout();
        bool right=family.Contains("right");double sign=right?1:-1;
        double speed=family.Contains("faster")?100:family.Contains("slow")?35:60;
        _moveVelocity=new Vector(sign*speed,0);
        if(family.Contains("climb.top")){Top=WorkingArea().Top;_moveVelocity=new Vector(sign*40,0);}
        else if(family.Contains("climb")){SnapSide(right);_moveVelocity=new Vector(0,Top-WorkingArea().Top>80?-45:45);}
        else if(family.Contains("fall"))_moveVelocity=new Vector(sign*55,90);
        _motionAt=DateTimeOffset.UtcNow;
    }
    private void SnapSide(bool right)
    {UpdateLayout();var area=WorkingArea();var bounds=VisibleHorizontalBounds();Left=right?area.Right-bounds.Right*WindowScale.ScaleX:area.Left-bounds.Left*WindowScale.ScaleX;ClampPosition();}
    private void HideAtSide(string family)
    { StartAction(family,0);_sideHideFamily=family.Replace("_Rise","_Main");SnapSide(family.Contains("Right"));Say("靠近我会探头，点我就回来啦。",false); }
    private void SidePeek(bool peek)
    {
        if(_sideHideFamily is null||_dragging)return;
        StartAction(peek?_sideHideFamily.Replace("_Main","_Rise"):_sideHideFamily,0,prepare:false);
        SnapSide(_sideHideFamily.Contains("Right"));
    }
    private void MotionTick()
    {
        var now=DateTimeOffset.UtcNow;double seconds=Math.Clamp((now-_motionAt).TotalSeconds,0,.1);_motionAt=now;
        if(_dragging)
        {
            var cursor=System.Windows.Forms.Cursor.Position;
            bool moved=Math.Abs(cursor.X-_dragCursor.X)+Math.Abs(cursor.Y-_dragCursor.Y)>3;_dragCursor=cursor;
            string family=moved?"Raise/Raised_Dynamic":"Raise/Raised_Static";
            if(_actionFamily!=family)StartAction(family,0,prepare:false);
            return;
        }
        if(_moveVelocity.Length<.01||!IsVisible)return;
        double x=Left,y=Top;
        Left+=_moveVelocity.X*seconds*WindowScale.ScaleX;Top+=_moveVelocity.Y*seconds*WindowScale.ScaleY;
        ClampPosition();
        if(Math.Abs(Left-x)<.01&&Math.Abs(Top-y)<.01){EndAction();SavePosition();}
    }
    private void FeedPet(PetFood food)
    {
        if(_settings.Life.Coins<food.Price){Say("宠物金币不够，工作可以赚金币。");return;}
        PrepareInteraction();
        int generation=++_actionGeneration;_actionFamily="food";
        // Apply the item once only after the animation completes; interruption does not consume it.
        _animation.PlayFood(_catalog.Recipe(food.Graph,Mood),food.File,()=>
        {
            if(generation!=_actionGeneration)return;
            _settings.Life.Feed(food,out var message);_settings.Save();_actionFamily=null;ReturnToState();Say(message,false);
        });
    }
    private void StartActivity(PetActivity activity,double? minutes=null)
    {
        StartAction(activity.Family,0);
        _activity=activity;_activityStart=DateTimeOffset.UtcNow;
        _activityEnd=_activityStart.AddMinutes(minutes??activity.Minutes);
        FocusBadge.Visibility=Visibility.Visible;FocusCaption.Text=activity.Name;
        Say($"陪你{activity.Name}。随时可以停止。",false);TickPetLife();
    }
    private void FinishActivity(bool completed)
    {
        if(_activity is not {} activity)return;
        _activity=null; // settle only once, including cancellation and shutdown
        double elapsed=(DateTimeOffset.UtcNow-_activityStart).TotalMinutes;
        _settings.Life.Complete(activity,Math.Clamp(elapsed/activity.Minutes,0,1));_settings.Save();
        FocusBadge.Visibility=Visibility.Collapsed;
        if(completed)Say($"{activity.Name}完成啦！",false);
    }
    private void PlayMusic()
    {
        var dialog=new OpenFileDialog {Title="选择本地音乐",Filter="音乐文件|*.mp3;*.wav;*.wma;*.m4a;*.aac|所有文件|*.*"};
        if(dialog.ShowDialog()!=true)return;
        StartAction("Music",0);_musicLoop=true;_music.Open(new Uri(dialog.FileName));_music.Volume=.35;_music.Play();
        Say("一起听歌吧 ♫",false);
    }
    private void CelebrateBirthday()
    {
        StartAction("BDay",8);Say("生日快乐！愿每一天都有小惊喜 ♡",false);
    }
    private void PlayExactClip(PetClip clip)
    {
        PrepareInteraction();_actionFamily="preview";int generation=++_actionGeneration;
        _animation.Play("@"+clip.Id,false,()=>{if(generation==_actionGeneration){_actionFamily=null;ReturnToState();}});
    }
    private void PetStartup()
    {
        StartAction("StartUP",3,()=>
        {
            ReturnToState();var today=DateTimeOffset.Now;
            if(today.Month==_settings.Life.AdoptedAt.ToLocalTime().Month&&today.Day==_settings.Life.AdoptedAt.ToLocalTime().Day&&_settings.Life.LastBirthday!=today.ToString("yyyy-MM-dd"))
            {_settings.Life.LastBirthday=today.ToString("yyyy-MM-dd");_settings.Save();CelebrateBirthday();}
        });
    }
    private bool _exitRequested;
    private void ExitWithAnimation()
    {
        if(_exitRequested)return;ShowPet();
        StartAction("Shutdown",2,Close);_exitRequested=true;
    }
}
