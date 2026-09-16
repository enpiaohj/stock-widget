using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StockWidget.Core.Services;

namespace StockWidget.App.Views;

/// <summary>
/// 同花顺风格分时图：上部价格区（约 74%）+ 下部分钟量能区（约 22%）。
/// 价格线白色、昨收红色虚线基线、右侧价格轴、底部时间轴；分钟量按相对前一分钟涨跌红/绿配色。
/// </summary>
public sealed class MinuteChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<MinutePoint>), typeof(MinuteChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PrevCloseProperty = DependencyProperty.Register(
        nameof(PrevClose), typeof(decimal?), typeof(MinuteChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<MinutePoint>? Points
    {
        get => (IReadOnlyList<MinutePoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>昨日收盘价（可选），决定基线位置与量能配色参照。</summary>
    public decimal? PrevClose
    {
        get => (decimal?)GetValue(PrevCloseProperty);
        set => SetValue(PrevCloseProperty, value);
    }

    private static Brush T(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var points = Points;
        if (points is not { Count: >= 2 })
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            return;
        }

        // 布局：价格区 74%，量能区 22%，中间 4% 间隔
        var priceH = h * 0.74;
        var volTop = h * 0.78;
        var volH = h - volTop;
        const double gridPad = 2;

        var prices = new decimal[points.Count];
        for (var i = 0; i < points.Count; i++) prices[i] = points[i].Price;
        var high = prices.Max();
        var low = prices.Min();

        // 同花顺规则：昨收虚线固定在价格区中央（=0%），上下幅度对称（取最大偏离）
        var pc = PrevClose ?? prices[0];
        var maxDev = Math.Max(Math.Abs(high - pc), Math.Abs(low - pc));
        if (maxDev <= 0) maxDev = pc * 0.01m + 0.0001m;
        var maxP = pc + maxDev;
        var minP = pc - maxDev;
        var range = maxP - minP;

        double Y(decimal p) => priceH - gridPad - (double)((p - minP) / range) * (priceH - gridPad * 2);

        // 网格：3 竖 + 3 横淡线
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)), 1);
        gridPen.Freeze();
        for (var g = 1; g <= 3; g++)
        {
            var x = w * g / 4;
            dc.DrawLine(gridPen, new Point(x, 0), new Point(x, priceH));
            var y = priceH * g / 4;
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }

        // 昨收虚线基线（红色）
        if (pc is { } baseline)
        {
            var baselinePen = new Pen(T("BaselineBrush"), 1) { DashStyle = DashStyles.Dash };
            baselinePen.Freeze();
            var yb = Y(baseline);
            dc.DrawLine(baselinePen, new Point(0, yb), new Point(w, yb));
        }

        // 价格折线（白色，同花顺分时线）
        var linePen = new Pen(Brushes.White, 1.5);
        linePen.Freeze();
        // 全天 241 个分钟槽位（上午121+下午120）：按时间映射 x，未到时间右侧留白
        var slotW = w / TotalSlots;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            for (var i = 0; i < points.Count; i++)
            {
                var slot = SlotOf(points[i].Time);
                if (slot < 0) continue;
                var x = (slot + 0.5) * slotW;
                if (!started) { ctx.BeginFigure(new Point(x, Y(prices[i])), false, false); started = true; }
                else ctx.LineTo(new Point(x, Y(prices[i])), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, linePen, geo);

        // 分钟量能柱：相对前一分钟价，红涨绿跌
        var upBrush = T("UpBrush");
        var downBrush = T("DownBrush");
        var maxVol = Math.Max(1m, points.Max(p => p.Volume));
        var barW = Math.Max(1d, slotW * 0.8);
        for (var i = 0; i < points.Count; i++)
        {
            var pt = points[i];
            var slot = SlotOf(pt.Time);
            if (pt.Volume <= 0 || slot < 0) continue;
            var up = i == 0
                ? pc is { } p0 ? pt.Price >= p0 : true
                : pt.Price >= points[i - 1].Price;
            var bh = (double)(pt.Volume / maxVol) * (volH - 2);
            dc.DrawRectangle(up ? upBrush : downBrush, null,
                new Rect(slot * slotW + slotW * 0.1, h - bh, barW, bh));
        }

        // 左轴=价格（昨收±幅度 + 当日最高/最低，重叠去重），右轴=百分比（0% 中央）——同花顺参数
        var axisBrush = T("SubFgBrush");
        var devPct = maxDev / pc * 100m;
        var labels = new List<(double Y, string Text, Brush B)>();
        void AddLabel(double y, string text, Brush b)
        {
            y = Math.Clamp(y, 0, priceH - 14);
            foreach (var (oy, _, _) in labels)
                if (Math.Abs(oy - y) < 12) return; // 重叠去重
            labels.Add((y, text, b));
        }
        AddLabel(0, Formatted(maxP), T("UpBrush"));
        AddLabel(Y(pc) - 7, Formatted(pc), axisBrush);
        AddLabel(Y(high) - 7, Formatted(high), T("UpBrush"));
        AddLabel(Y(low) - 7, Formatted(low), T("DownBrush"));
        AddLabel(priceH - 14, Formatted(minP), T("DownBrush"));
        foreach (var (y, text, b) in labels)
            DrawAxisText(dc, text, 2, y, TextAlignment.Left, b);
        DrawAxisText(dc, $"+{devPct:0.0#}%", w - 2, 0, TextAlignment.Right, T("UpBrush"));
        DrawAxisText(dc, "0.00%", w - 2, Math.Clamp(Y(pc) - 7, 0, priceH - 14), TextAlignment.Right, axisBrush);
        DrawAxisText(dc, $"-{devPct:0.0#}%", w - 2, priceH - 14, TextAlignment.Right, T("DownBrush"));


        // 底部时间轴：当前量 / 09:30 / 11:30-13:00 / 15:00（量值与时间对齐）
        var timeBrush = T("SubFgBrush");
        DrawAxisText(dc, $"量 {points[^1].Volume:N0}", 2, priceH + (volTop - priceH) / 2 - 7, TextAlignment.Left, timeBrush);
        DrawAxisText(dc, "09:30", 96, priceH + (volTop - priceH) / 2 - 7, TextAlignment.Left, timeBrush);
        DrawAxisText(dc, "11:30/13:00", w / 2, priceH + (volTop - priceH) / 2 - 7, TextAlignment.Center, timeBrush);
        DrawAxisText(dc, "15:00", w, priceH + (volTop - priceH) / 2 - 7, TextAlignment.Right, timeBrush);

        static string Formatted(decimal v) => v.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>分时全天槽位数：上午 121 分钟（9:30-11:30）+ 下午 120 分钟（13:00-15:00）。</summary>
    public const int TotalSlots = 241;

    /// <summary>"HHmm" → 全天槽位（0..240）；解析失败返回 -1。</summary>
    private static int SlotOf(string time)
    {
        if (string.IsNullOrEmpty(time) || time.Length < 4 || !int.TryParse(time, out var t)) return -1;
        var mins = t / 100 * 60 + t % 100;
        if (mins is >= 570 and <= 690) return mins - 570;         // 上午 9:30-11:30
        if (mins is >= 780 and <= 900) return 121 + (mins - 780); // 下午 13:00-15:00
        return -1;
    }

    /// <summary>槽位索引 → "HHmm"（快照回退分支构造时间用）。</summary>
    public static string SlotTimeOf(int index)
    {
        var slot = Math.Clamp(index, 0, TotalSlots - 1);
        var mins = slot <= 120 ? 570 + slot : 780 + (slot - 121);
        return (mins / 60 * 100 + mins % 60).ToString(CultureInfo.InvariantCulture);
    }

    private static void DrawAxisText(DrawingContext dc, string text, double x, double y,
        TextAlignment align, Brush brush)
    {
        if (string.IsNullOrEmpty(text) || y < 0) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("微软雅黑"), 9, brush, 1.25);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(x, y));
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
