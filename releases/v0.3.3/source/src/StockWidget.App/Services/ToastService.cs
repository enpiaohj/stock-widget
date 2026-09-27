using Hardcodet.Wpf.TaskbarNotification;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.App.Services;

/// <summary>托盘气泡通知（Win10/11 自动渲染为系统 Toast）。</summary>
public static class ToastService
{
    private static TaskbarIcon? _tray;

    public static void Init(TaskbarIcon tray) => _tray = tray;

    public static void Show(string title, string message)
    {
        if (_tray is null) return;
        try
        {
            _tray.ShowBalloonTip(title, message, BalloonIcon.Info);
        }
        catch
        {
            // 通知失败不影响主流程
        }
    }

    /// <summary>预警合并展示（最多列 3 条，避免刷屏）。</summary>
    public static void ShowAlerts(IReadOnlyList<AlertTrigger> alerts)
    {
        if (_tray is null || alerts.Count == 0) return;

        var lines = alerts.Take(3).Select(a =>
        {
            var dir = a.ChangePct >= 0 ? "涨" : "跌";
            return $"{a.Name}（{StockCodeNormalizer.StripPrefix(a.Code)}）{dir} {Math.Abs(a.ChangePct):0.00}%";
        });
        var text = string.Join("\n", lines);
        if (alerts.Count > 3) text += $"\n…等 {alerts.Count} 条预警";

        try
        {
            _tray.ShowBalloonTip("📈 涨跌幅预警", text, BalloonIcon.Info);
        }
        catch
        {
            // 通知失败不影响主流程
        }
    }
}
