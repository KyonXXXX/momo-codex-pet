using System;
using System.Globalization;

namespace Momo;

public partial class PetWindow
{
    private string DailyScope => (_usage?.Main?.Id ?? "") + ":" + (_usage?.Main?.Plan ?? "");
    private void RenderDailyUsage()
    {
        var now = DateTimeOffset.UtcNow;
        bool fresh = !_failed && _usage is not null && now - _usage.FetchedAt <= TimeSpan.FromMinutes(2);
        var progress = _dailyUsage.Calculate(_usage?.Main?.Weekly, now, TimeZoneInfo.Local, DailyScope, fresh);
        if (progress is null)
        {
            DailyUsedText.Text = "今日已用 —%"; DailyRemainingText.Text = "剩余 —%"; DailyFill.Width = 0;
            DailyRow.ToolTip = "等待有效周额度和重置时间同步；新的一天会重新记录，未知用量不显示为零。";
            DailyTrack.ToolTip = DailyRow.ToolTip;
            return;
        }
        double percent = Math.Round(progress.Percent, 1);
        string used = percent > 999 ? ">999" : (progress.Partial ? "≥" : "") + percent.ToString("0.#", CultureInfo.InvariantCulture);
        string remaining = Math.Round(progress.RemainingPercent, 1).ToString("0.#", CultureInfo.InvariantCulture);
        DailyUsedText.Text = $"今日已用 {used}%";
        DailyRemainingText.Text = $"剩余{(progress.Partial ? "≤" : " ")}{remaining}%";
        DailyFill.Width = 138 * Math.Clamp(progress.Percent, 0, 100) / 100;
        DailyFill.Background = Brush(progress.Percent > 100 ? "#D9A26E" : "#77B99B");
        DailyUsedText.Foreground = Brush(progress.Percent > 100 ? "#B78147" : "#3F9275");
        DailyRow.ToolTip = $"今日进度按周额度同步差额估算，不是账户额外发放的日限额。\n当前每日参考量：周剩余额度 ÷ 距重置的剩余天数（不足 1 天按 1 天）。\n参考量 {progress.Budget:0.##}%、今天已记录 {progress.Used:0.##}%，单位均为每周总额度。"
            + (progress.Partial ? $"\n今天从 {progress.StartedAt.ToLocalTime():HH:mm} 开始记录，此前用量无法补查；≥/≤ 表示记录范围内的下限/上限。" : "\n每天按电脑本地日期重新累计；周窗口重置或读数回退时重新记录。")
            + (fresh ? "" : "\n当前离线，保留上次成功同步的今日进度。")
            + (progress.Percent > 100 ? "\n已超过当前每日参考量，进度条显示为提醒色。" : "");
        DailyTrack.ToolTip = DailyRow.ToolTip;
    }
}
