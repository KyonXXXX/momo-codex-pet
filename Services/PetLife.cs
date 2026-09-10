using System;

namespace Momo;

public sealed class PetLife
{
    public int Version { get; set; } = 1;
    public double Energy { get; set; } = 85;
    public double Hunger { get; set; } = 85;
    public double Thirst { get; set; } = 85;
    public double Feeling { get; set; } = 85;
    public double Health { get; set; } = 100;
    public double Affection { get; set; } = 10;
    public double Coins { get; set; } = 300;
    public double Experience { get; set; }
    public DateTimeOffset AdoptedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastBirthday { get; set; }
    public int Level => (int)(Math.Sqrt(Math.Max(0, Experience)) / 10) + 1;
    public string Mood => Health < 30 ? "ill" : Feeling < 30 || Energy < 20 ? "poorcondition" : Feeling >= 70 && Health >= 60 ? "happy" : "nomal";
    public void Normalize()
    {
        static double Stat(double n, double fallback) => double.IsFinite(n) ? Math.Clamp(n, 0, 100) : fallback;
        Energy=Stat(Energy,85);Hunger=Stat(Hunger,85);Thirst=Stat(Thirst,85);Feeling=Stat(Feeling,85);Health=Stat(Health,100);Affection=Stat(Affection,10);
        Coins=double.IsFinite(Coins)?Math.Clamp(Coins,0,1e9):300;Experience=double.IsFinite(Experience)?Math.Clamp(Experience,0,1e9):0;
    }
    public void Tick(double seconds, bool sleeping, PetActivity? activity)
    {
        // Only elapsed running time counts. Absence and suspended computers incur no penalty.
        double minutes=Math.Clamp(seconds,0,5)/60;
        Hunger-=minutes*(.15+(activity?.HungerCost??0));Thirst-=minutes*(.2+(activity?.ThirstCost??0));
        Energy+=minutes*(sleeping?3:activity is null?-.12:-.6);
        Feeling-=minutes*(activity?.FeelingCost??.05);
        if(Hunger<10||Thirst<10)Health-=minutes*.8;
        else if(sleeping)Health+=minutes*.8;
        Normalize();
    }
    public bool Feed(PetFood food, out string message)
    {
        if(Coins<food.Price){message="宠物金币不够，工作可以赚金币。";return false;}
        Coins-=food.Price;Energy+=food.Energy;Hunger+=food.Hunger;Thirst+=food.Thirst;Health+=food.Health;Feeling+=food.Feeling;Experience+=food.Exp;Affection+=food.Affection;
        Normalize();message=$"收到{food.Name}啦 ♡";return true;
    }
    public void Complete(PetActivity activity, double fraction=1)
    {
        double progress=Math.Clamp(fraction,0,1),bonus=progress>=1?1+activity.Bonus:1;
        if(activity.Type=="Work")Coins+=activity.Reward*activity.Minutes*progress*bonus;
        else if(activity.Type=="Study")Experience+=Math.Max(10,activity.Reward)*activity.Minutes*progress*bonus;
        else {Feeling+=20*progress;Experience+=10*progress;}
        Normalize();
    }
}
