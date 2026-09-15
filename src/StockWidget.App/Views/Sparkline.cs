using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StockWidget.App.Views;

/// <summary>
/// 分时迷你走势（Sparkline）：StreamGeometry 折线，涨红跌绿。
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<decimal>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>昨日收盘价（可选），用于更精确的涨跌配色；null 时按首尾比较。</summary>
    public static readonly DependencyProperty BaselineProperty = DependencyProperty.Register(
        nameof(Baseline), typeof(decimal?), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<decimal>? Values
    {
        get => (IReadOnlyList<decimal>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public decimal? Baseline
    {
        get => (decimal?)GetValue(BaselineProperty);
        set => SetValue(BaselineProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var values = Values;
        if (values is not { Count: >= 2 })
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            return;
        }

        decimal min = values.Min(), max = values.Max();
        if (max == min) { max += 0.0001m; min -= 0.0001m; }

        // 单次遍历收集折线点 + 记录末点
        var pts = new Point[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * w / (values.Count - 1);
            var y = h - 2 - (float)((values[i] - min) / (max - min)) * (h - 4);
            pts[i] = new Point(x, y);
        }
        var lastPt = pts[^1];

        var lastUp = Baseline is { } bl ? values[^1] >= bl : values[^1] >= values[0];
        var colorKey = lastUp ? "UpBrush" : "DownBrush";
        var brush = Application.Current.Resources[colorKey] as Brush ?? Brushes.Gray;
        var lineColor = brush is SolidColorBrush sb ? sb.Color : Colors.Gray;
        var pen = new Pen(brush, 1.2);
        pen.Freeze();

        // 面积渐变填充（顶部半透明折线色渐隐）
        var fillBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(0x3C, lineColor.R, lineColor.G, lineColor.B), 0),
                new(Color.FromArgb(0x00, lineColor.R, lineColor.G, lineColor.B), 1),
            },
        };
        fillBrush.Freeze();

        var areaGeo = new StreamGeometry();
        using (var aCtx = areaGeo.Open())
        {
            aCtx.BeginFigure(pts[0], true, false);
            for (var i = 1; i < pts.Length; i++) aCtx.LineTo(pts[i], true, false);
            aCtx.LineTo(new Point(w, h), true, false);
            aCtx.LineTo(new Point(0, h), true, false);
        }
        areaGeo.Freeze();
        dc.DrawGeometry(fillBrush, null, areaGeo);

        // 折线本体
        var lineGeo = new StreamGeometry();
        using (var ctx = lineGeo.Open())
        {
            ctx.BeginFigure(pts[0], false, false);
            for (var i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
        }
        lineGeo.Freeze();
        dc.DrawGeometry(null, pen, lineGeo);

        // 末点光标
        dc.DrawEllipse(Brushes.White, new Pen(brush, 1.2), lastPt, 2.1, 2.1);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
