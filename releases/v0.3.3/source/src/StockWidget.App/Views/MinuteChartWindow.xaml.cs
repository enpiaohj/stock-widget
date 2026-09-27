using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using StockWidget.App.Services;
using StockWidget.App.ViewModels;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;

namespace StockWidget.App.Views;

/// <summary>当日分时走势弹窗（行情行双击打开）。</summary>
public partial class MinuteChartWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private StockRowViewModel _row;
    private bool _showingKline;
    private readonly AiAnalysisPanel? _aiPanel;

    // ---------------------------
    // AI 分析侧面板：以初始窗口宽度为基础，展开时窗口向右扩展 AiPanelWidth
    // ---------------------------
    private bool _aiPanelOpen;
    private const double AiPanelWidth = 320;
    private const double BaseWidth = 720;   // 与 XAML 初始 Width 一致

    public MinuteChartWindow(MainViewModel vm, StockRowViewModel row)
    {
        _vm = vm;
        _row = row;
        InitializeComponent();

        LoadHeader(row);

        // AI 面板（设置中未启用时按钮点击会给出引导）
        var aiService = App.Services.GetRequiredService<IAiAnalysisService>();
        var settingsService = App.Services.GetRequiredService<ISettingsService>();
        _aiPanel = new AiAnalysisPanel(aiService, settingsService);
        _aiPanel.RequestOpenSettings += () => _vm.RequestSettings("ai");
        AiPanelHostContent.Content = _aiPanel;

        Loaded += async (_, _) => await LoadAsync();
        Closing += (_, _) => _aiPanel.Cancel();
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

    // ---------------------------
    // AI 分析（✨ AI分析按钮：展开/收起侧面板）
    // ---------------------------

    private void AiAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (_aiPanel is null) return;
        _aiPanelOpen = !_aiPanelOpen;
        AiPanelHost.Visibility = _aiPanelOpen ? Visibility.Visible : Visibility.Collapsed;

        if (!_fullScreen)
            Width = BaseWidth + (_aiPanelOpen ? AiPanelWidth : 0); // 初始宽度 + 面板宽度，无累加

        if (_aiPanelOpen)
            _aiPanel.Analyze(BuildContext);
    }

    /// <summary>组装本地行情分析上下文（后台线程执行；数据全部来自本地）。</summary>
    private MarketAnalysisContext? BuildContext()
    {
        var quote = _row.Current;
        var klines = _vm.GetKlines(_row.Code, 120); // 与 K 线图可见区间一致
        if (quote is not { Success: true } || klines.Count == 0) return null;

        decimal Ma(int n) => klines.TakeLast(n).Average(k => k.Close);
        decimal Chg(int n) => klines.Count > n && klines[^(n + 1)].Close != 0
            ? (klines[^1].Close / klines[^(n + 1)].Close - 1m) * 100m
            : 0m;

        return new MarketAnalysisContext
        {
            Code = _row.Code,
            Name = quote.Name is "-" or "" ? _row.Info.Name : quote.Name,
            AssetType = _row.CategoryName,
            AnalyzedAt = DateTime.Now,
            Quote = quote,
            Klines = klines,
            Ma5 = Ma(5),
            Ma10 = Ma(10),
            Ma20 = Ma(20),
            Change5Pct = Chg(5),
            Change20Pct = Chg(20),
            Change60Pct = Chg(60),
            RangeHigh = klines.Max(k => k.High),
            RangeLow = klines.Min(k => k.Low),
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    // ---------------------------
    // 全屏 / 还原（铺满工作区；记录原位置便于还原）
    // ---------------------------

    private bool _fullScreen;
    private Rect _restoreBounds = new(0, 0, 720, 440);
    private bool _restoreTopmost;

    private void FullScreen_Click(object sender, RoutedEventArgs e) => SetFullScreen(!_fullScreen);

    private void SetFullScreen(bool full)
    {
        var work = SystemParameters.WorkArea;
        if (full)
        {
            // 记录"非全屏目标状态"（由状态计算：初始宽度 + 面板宽度），不读当前 UI 值，防漂移
            _restoreBounds = new Rect(Left, Top, BaseWidth + (_aiPanelOpen ? AiPanelWidth : 0), 440);
            _restoreTopmost = Topmost;
            WindowState = WindowState.Normal; // 从最小化状态还原后再铺满
            Left = work.Left;
            Top = work.Top;
            Width = work.Width;
            Height = work.Height;
            Topmost = true;
            FullScreenButton.Content = "❐ 还原";
            FullScreenButton.ToolTip = "还原";
        }
        else
        {
            WindowState = WindowState.Normal;
            Left = _restoreBounds.Left;
            Top = _restoreBounds.Top;
            Width = Math.Clamp(BaseWidth + (_aiPanelOpen ? AiPanelWidth : 0), 400, work.Width);
            Height = Math.Clamp(440, 300, work.Height);
            Topmost = _restoreTopmost;
            FullScreenButton.Content = "⛶ 全屏";
            FullScreenButton.ToolTip = "全屏";
        }
        _fullScreen = full;
    }

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
            VolumeText.Text = $"量 {minute.Points[^1].Volume:N0}";
        }
        catch
        {
            ClosedBadge.Visibility = Visibility.Visible;
        }
    }
}
