using System;
using System.Globalization;

namespace Momo;

public partial class PetWindow
{
    private void RenderDailyUsage()
    {
        var now = DateTimeOffset.UtcNow;
        bool fresh = !_failed && _usage is not null && now - _usage.FetchedAt <= TimeSpan.FromMinutes(2);
        var progress = _usage is null ? null : DailyAllowance.ForDisplay(_usage.Main?.Weekly, _usage.FetchedAt, now, fresh);
        if (progress is null)
        {
            DailyLabel.Text = "今日剩余可用";
            DailyLabel.Foreground = DailyRemainingText.Foreground = Brush("#3F9275");
            DailyRemainingText.Text = "—%"; DailyFill.Width = 0;
            DailyRow.ToolTip = "等待有效周额度同步后计算今日剩余可用额度；离线跨日或周重置后需重新同步。";
            DailyTrack.ToolTip = DailyRow.ToolTip;
            return;
        }
        RenderDailyProgress(progress, fresh);
    }

    private void RenderDailyProgress(DailyAllowance progress, bool fresh)
    {
        bool overdrawn = progress.IsOverdrawn;
        double displayed = overdrawn ? progress.OverdrawnPercent : progress.Percent;
        DailyLabel.Text = overdrawn ? "今日已超额" : "今日剩余可用";
        DailyRemainingText.Text = overdrawn && displayed < .005 ? "<0.01%" : displayed.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        DailyFill.Width = 138 * Math.Clamp(displayed, 0, 100) / 100;
        bool depleted = progress.Percent <= 15;
        DailyFill.Background = Brush(overdrawn ? "#D67B7B" : depleted ? "#D9A26E" : "#77B99B");
        DailyLabel.Foreground = DailyRemainingText.Foreground = Brush(overdrawn ? "#B94F5F" : depleted ? "#B78147" : "#3F9275");
        DailyRow.ToolTip = $"100% = 周总额度的 1/7（约 {DailyAllowance.BaseDaily:0.##}%）。"
            + (overdrawn ? $"\n截至今天已超出分配进度，提前使用周总额度的 {progress.OverdrawnWeeklyPercent:0.####}%。\n包含前几天结转的透支，不代表仅今天实际消耗的额度。"
                : $"\n今天还可使用周总额度的 {progress.AvailableWeeklyPercent:0.##}%。")
            + $"\n当前周剩余 {progress.WeeklyRemaining:0.##}%，距重置还有 {progress.DaysRemaining} 个额度日（含今天）。"
            + "\n透支优先抵扣今天，不足则继续抵扣后续天；有结余时按剩余天数均分。"
            + "\n按账户周重置时间划分 7 个 24 小时额度日，与电脑时区无关。"
            + "\n根据账号最新周额度推算，包含其他电脑的使用；这是分配参考，不是官方日限额。"
            + (fresh ? "" : "\n当前离线，显示上次同步结果；重新同步后会包含其他电脑的最新用量。")
            + (overdrawn ? "\n红色进度条表示已超额比例，最多满格；数字保留实际比例。" : "")
            + (progress.Percent > 100 ? "\n有结余，今天可用量超过一天基础额度；进度条最多显示满格。" : "");
        DailyTrack.ToolTip = DailyRow.ToolTip;
    }
}
