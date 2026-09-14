using System.Windows;
using System.Windows.Media;
using StockWidget.App.ViewModels;
using StockWidget.Core.Models;

namespace StockWidget.App.Views;

/// <summary>当日分时走势弹窗（行情行双击打开）。</summary>
public partial class MinuteChartWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private readonly StockRowViewModel _row;

    public MinuteChartWindow(MainViewModel vm, StockRowViewModel row)
    {
        _vm = vm;
        _row = row;
        InitializeComponent();

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

        Loaded += async (_, _) => await LoadAsync();
    }

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
}
