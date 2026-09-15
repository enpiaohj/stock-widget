using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StockWidget.App.ViewModels;
using StockWidget.Core.Models;

namespace StockWidget.App.Views;

/// <summary>当日分时走势弹窗（行情行双击打开）。</summary>
public partial class MinuteChartWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private StockRowViewModel _row;

    public MinuteChartWindow(MainViewModel vm, StockRowViewModel row)
    {
        _vm = vm;
        _row = row;
        InitializeComponent();

        LoadHeader(row);

        Loaded += async (_, _) => await LoadAsync();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close(); // Esc 关闭
        };
    }

    /// <summary>复用已打开的窗口切换到另一只股票（避免越开越多）。</summary>
    public void ShowFor(StockRowViewModel row)
    {
        _row = row;
        LoadHeader(row);
        _ = LoadAsync();
        Activate();
    }

    private void LoadHeader(StockRowViewModel row)
    {
        var quote = row.Current;
        NameText.Text = quote.Success ? quote.Name : row.Info.Name;
        CodeText.Text = $"{row.Code}（{row.CodeDisplay}）";

        if (quote.Success && quote.Price is { } price)
        {
            PriceText.Text = $"{price:0.###}";
            var change = quote.ChangePct ?? 0m;
            ChangeText.Text = $"{(change >= 0 ? "+" : "")}{change:0.00}%";
            var brush = (Brush)Application.Current.Resources[change switch
            {
                > 0 => "UpBrush",
                < 0 => "DownBrush",
                _ => "FlatBrush",
            }];
            PriceText.Foreground = brush;
            ChangeText.Foreground = brush;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadAsync()
    {
        try
        {
            var minute = await _vm.GetMinuteLineAsync(_row.Code);
            if (minute is not { Points.Count: > 1 })
            {
                ClosedBadge.Visibility = Visibility.Visible;
                // 无分时数据时回退显示当日快照
                var snapshots = _vm.GetTodaySnapshots(_row.Code);
                if (snapshots.Count >= 2)
                {
                    Chart.Values = snapshots;
                    ClosedBadge.Visibility = Visibility.Collapsed;
                }
                return;
            }

            Chart.Values = minute.Points.Select(p => p.Price).ToList();
            Chart.Baseline = minute.PrevClose ?? _vm.GetPrevClose(_row.Code);

            var prices = minute.Points.Select(p => p.Price).ToList();
            PositionBaseline(prices);
            HighText.Text = $"最高 {prices.Max():0.###}";
            LowText.Text = $"最低 {prices.Min():0.###}";
            TimeText.Text = $"{minute.Date} · 分时";
            if (minute.PrevClose is { } pc)
                PrevCloseText.Text = $"昨收 {pc:0.###} · 虚线为分时基线";
        }
        catch
        {
            ClosedBadge.Visibility = Visibility.Visible;
        }
    }

    /// <summary>依据昨收与当日价区间定位基准虚线的 Y 位置。</summary>
    private void PositionBaseline(List<decimal> prices)
    {
        var bl = Chart.Baseline;
        if (bl is not { } pc || prices.Count < 2 || Chart.ActualHeight <= 0)
        {
            BaselineBar.Visibility = Visibility.Collapsed;
            return;
        }
        BaselineBar.Visibility = Visibility.Visible;
        var mn = prices.Min(); var mx = prices.Max();
        if (mx == mn) mx += 0.0001m;
        var ratio = (float)((pc - mn) / (mx - mn));
        // 与 Sparkline 的坐标映射一致：y = h-2 - ratio*(h-4)；此处取中线定位
        var top = (Chart.ActualHeight - 2) * (1 - ratio) - 0.5;
        BaselineBar.Margin = new Thickness(0, Math.Max(0, top), 0, 0);
    }
}
