using System.Windows;
using System.Windows.Input;
using StockWidget.App.ViewModels;

namespace StockWidget.App.Views;

/// <summary>沪深成交额趋势弹窗（底栏双击 / 右键菜单打开）。</summary>
public partial class AmountTrendWindow : GlassWindow
{
    private readonly MainViewModel _vm;

    public AmountTrendWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        Loaded += async (_, _) => await LoadAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(); // Esc 关闭
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            var rows = await Task.Run(() => _vm.GetRecentAmounts(60));
            Chart.Amounts = rows;
            if (rows.Count > 0)
            {
                var last = rows[^1];
                var prev = rows.Count > 1 ? rows[^2].Total : 0m;
                var diff = rows.Count > 1 ? last.Total - prev : 0m;
                var ma5 = rows.Count >= 5 ? rows.TakeLast(5).Average(r => r.Total) : 0m;
                var ma20 = rows.Count >= 20 ? rows.TakeLast(20).Average(r => r.Total) : 0m;
                var window = rows.TakeLast(60).ToList();
                var rank = window.Count(r => r.Total > last.Total) + 1;
                StatsText.Text = $"今日总量 {last.Total:N0} 亿（较昨{(diff >= 0 ? "增" : "缩")} {Math.Abs(diff):N0} 亿）　" +
                                 $"5日均值 {ma5:N0} 亿　20日均值 {ma20:N0} 亿　" +
                                 $"今日为近 {window.Count} 日第 {rank} 高";
            }
        }
        catch
        {
            StatsText.Text = "成交额数据加载失败";
        }
    }
}
