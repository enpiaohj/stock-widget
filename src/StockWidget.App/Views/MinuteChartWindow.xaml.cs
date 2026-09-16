using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StockWidget.App.ViewModels;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.App.Views;

/// <summary>当日分时走势弹窗（行情行双击打开）。</summary>
public partial class MinuteChartWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private StockRowViewModel _row;
    private bool _showingKline;

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
        if (_showingKline) _ = LoadKlinesAsync(); // 日K页保持展示时同步刷新另一只股票的日K
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

        // 同花顺风格信息行：最高 / 最低 / 开盘 / 昨收（缺失显示 —）
        static string F(decimal? v) => v?.ToString("0.###") ?? "—";
        InfoText.Text = $"最高 {F(quote.High)}　最低 {F(quote.Low)}　开盘 {F(quote.Open)}　昨收 {F(quote.PrevClose)}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TabMinute_Click(object sender, RoutedEventArgs e)
    {
        _showingKline = false;
        TabMinute.IsChecked = true;
        TabKline.IsChecked = false;
        MinutePanel.Visibility = Visibility.Visible;
        KlinePanel.Visibility = Visibility.Collapsed;
    }

    private void TabKline_Click(object sender, RoutedEventArgs e)
    {
        _showingKline = true;
        TabKline.IsChecked = true;
        TabMinute.IsChecked = false;
        MinutePanel.Visibility = Visibility.Collapsed;
        KlinePanel.Visibility = Visibility.Visible;
        _ = LoadKlinesAsync();
    }

    /// <summary>后台加载日K并计算区间涨跌标签。</summary>
    private async Task LoadKlinesAsync()
    {
        try
        {
            var code = _row.Code;
            // DB 查询不放 UI 线程
            var rows = await Task.Run(() => _vm.GetKlines(code, 250));
            KlineChart.Klines = rows;
            RangeTextK.Text = BuildRangeText(rows);
        }
        catch
        {
            RangeTextK.Text = "日K数据加载失败";
        }
    }

    /// <summary>近 5/20/60/250 日涨跌幅（最新收盘 / N日前收盘 - 1；根数不足显示 —）。</summary>
    private static string BuildRangeText(IReadOnlyList<DailyKlineEntity> rows)
    {
        string Part(int n)
        {
            if (rows.Count <= n || rows[^(n + 1)].Close == 0m) return $"近{n}日：—";
            var chg = (rows[^1].Close / rows[^(n + 1)].Close - 1m) * 100m;
            return $"近{n}日：{chg:+0.##;-0.##}%";
        }
        return string.Join("  ", Part(5), Part(20), Part(60), Part(250));
    }

    private async Task LoadAsync()
    {
        try
        {
            var minute = await _vm.GetMinuteLineAsync(_row.Code);
            if (minute is not { Points.Count: > 1 })
            {
                ClosedBadge.Visibility = Visibility.Visible;
                // 无分时数据时回退显示当日快照（量能不可用，按 0 处理）
                var snapshots = await Task.Run(() => _vm.GetTodaySnapshots(_row.Code));
                if (snapshots.Count >= 2)
                {
                    Chart.Points = snapshots.Select((p, i) => new MinutePoint(MinuteChart.SlotTimeOf(i), p, 0m)).ToList();
                    Chart.PrevClose = _vm.GetPrevClose(_row.Code);
                    ClosedBadge.Visibility = Visibility.Collapsed;
                }
                return;
            }

            Chart.Points = minute.Points;
            Chart.PrevClose = minute.PrevClose ?? _vm.GetPrevClose(_row.Code);

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
