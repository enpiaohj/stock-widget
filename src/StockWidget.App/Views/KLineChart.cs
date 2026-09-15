using System.Globalization;
using System.Windows;
using System.Windows.Media;
using StockWidget.Core.Data.Entities;

namespace StockWidget.App.Views;

/// <summary>
/// 日K蜡烛图：最近 N 根蜡烛（红涨绿跌）+ 底部成交量副图（1/4 高）+ MA5/10/20 均线。
/// </summary>
public sealed class KLineChart : FrameworkElement
{
    public static readonly DependencyProperty KlinesProperty = DependencyProperty.Register(
        nameof(Klines), typeof(IReadOnlyList<DailyKlineEntity>), typeof(KLineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>显示根数上限。</summary>
    public int VisibleBars { get; set; } = 120;

    public IReadOnlyList<DailyKlineEntity>? Klines
    {
        get => (IReadOnlyList<DailyKlineEntity>?)GetValue(KlinesProperty);
        set => SetValue(KlinesProperty, value);
    }

    private static Brush T(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var all = Klines;
        if (all is not { Count: > 1 } || w <= 0 || h <= 0) return;

        var bars = all.TakeLast(Math.Min(VisibleBars, all.Count)).ToList();
        var volH = h / 4;                              // 成交量副图高度
        var priceH = h - volH - 6;                     // 价格区高度
        var maxP = bars.Max(b => b.High);
        var minP = bars.Min(b => b.Low);
        if (maxP <= minP) maxP = minP + 0.0001m;
        var maxV = Math.Max(1m, bars.Max(b => b.Volume));

        var barW = w / bars.Count;

        // 均线（画在蜡烛下层）：MA5 白 / MA10 金 / MA20 紫
        DrawMa(dc, bars, 5, w, priceH, maxP, minP, Colors.White);
        DrawMa(dc, bars, 10, w, priceH, maxP, minP, Colors.Gold);
        DrawMa(dc, bars, 20, w, priceH, maxP, minP, Colors.MediumPurple);

        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var xC = (i + 0.5) * barW;
            var up = b.Close >= b.Open;
            var brush = T(up ? "UpBrush" : "DownBrush");
            double Y(decimal p) => priceH - (double)((p - minP) / (maxP - minP)) * (priceH - 4) - 2;

            // 影线
            dc.DrawLine(new Pen(brush, 1), new Point(xC, Y(b.High)), new Point(xC, Y(b.Low)));

            // 实体（开盘=收盘时画 1px 横线）
            var yO = Y(b.Open);
            var yC = Y(b.Close);
            var top = Math.Min(yO, yC);
            var bodyH = Math.Max(1, Math.Abs(yC - yO));
            var bodyW = Math.Max(1, barW * 0.6);
            dc.DrawRectangle(brush, null, new Rect(xC - bodyW / 2, top, bodyW, bodyH));

            // 成交量柱（底部副图）
            var vh = (double)(b.Volume / maxV) * (volH - 4);
            dc.DrawRectangle(brush, null, new Rect(xC - bodyW / 2, h - vh, bodyW, vh));
        }
    }

    private void DrawMa(DrawingContext dc, IReadOnlyList<DailyKlineEntity> bars, int n,
        double w, double priceH, decimal maxP, decimal minP, Color color)
    {
        if (bars.Count < n) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B)), 1);
        pen.Freeze();
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            for (var i = n - 1; i < bars.Count; i++)
            {
                var ma = bars.Skip(i + 1 - n).Take(n).Average(b => b.Close);
                var x = (i + 0.5) * w / bars.Count;
                var y = priceH - (double)((ma - minP) / (maxP - minP)) * (priceH - 4) - 2;
                if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
