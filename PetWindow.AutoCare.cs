using System;

namespace Momo;

public partial class PetWindow
{
    private AutoCare _autoCare = new();
    private string _lastAutoCare = "尚未自动购买";
    private Action? _refreshInteractionStats;

    private void TryAutoCare(long? nowMs = null)
    {
        var purchase = _autoCare.TryPurchase(_settings, _catalog.Foods, nowMs ?? Environment.TickCount64,
            _closing || _exitRequested || _dragging || _actionFamily == "food");
        if (purchase is null) return;
        _settings.Save();
        _lastAutoCare = $"{DateTimeOffset.Now:HH:mm:ss} · {purchase.StatName}不足，已购买{purchase.Food.Name}（{purchase.Food.Price:0.##} 金币）";
        _refreshInteractionStats?.Invoke();
        Say($"自动补充：{purchase.Food.Name} · -{purchase.Food.Price:0.##} 金币", false);
    }
}
