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

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var x = i * w / (values.Count - 1);
                var y = h - 2 - (float)((values[i] - min) / (max - min)) * (h - 4);
                if (i == 0) ctx.BeginFigure(new Point(x, y), false, false);
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();

        var lastUp = Baseline is { } bl ? values[^1] >= bl : values[^1] >= values[0];
        var colorKey = lastUp ? "UpBrush" : "DownBrush";
        var brush = Application.Current.Resources[colorKey] as Brush ?? Brushes.Gray;
        var pen = new Pen(brush, 1.2);
        pen.Freeze();

        dc.DrawGeometry(null, pen, geo);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
