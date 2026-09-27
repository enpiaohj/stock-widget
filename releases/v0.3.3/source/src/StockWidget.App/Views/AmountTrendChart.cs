using System.Windows;
using System.Windows.Media;
using StockWidget.Core.Data.Entities;

namespace StockWidget.App.Views;

/// <summary>
/// 沪深成交额趋势图：近 60 个交易日柱状（红=较前日放量、绿=缩量）+ 5/20 日均值折线。
/// </summary>
public sealed class AmountTrendChart : FrameworkElement
{
    public static readonly DependencyProperty AmountsProperty = DependencyProperty.Register(
        nameof(Amounts), typeof(IReadOnlyList<DailyAmountEntity>), typeof(AmountTrendChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<DailyAmountEntity>? Amounts
    {
        get => (IReadOnlyList<DailyAmountEntity>?)GetValue(AmountsProperty);
        set => SetValue(AmountsProperty, value);
    }

    private static Brush T(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var rows = Amounts;
        if (rows is not { Count: > 1 } || w <= 0 || h <= 0) return;

        var max = Math.Max(1m, rows.Max(r => r.Total));
        var barW = w / rows.Count;

        for (var i = 0; i < rows.Count; i++)
        {
            var up = i == 0 || rows[i].Total >= rows[i - 1].Total;
            var brush = T(up ? "UpBrush" : "DownBrush");
            var bh = (double)(rows[i].Total / max) * (h - 20) + 2;
            var bw = Math.Max(2, barW * 0.7);
            dc.DrawRectangle(brush, null, new Rect(i * barW + (barW - bw) / 2, h - bh, bw, bh));
        }

        // 5/20 日均值折线（画在柱上层）
        DrawMa(dc, rows, 5, w, h, Colors.Gold);
        DrawMa(dc, rows, 20, w, h, Colors.MediumPurple);
    }

    private void DrawMa(DrawingContext dc, IReadOnlyList<DailyAmountEntity> rows, int n,
        double w, double h, Color color)
    {
        if (rows.Count < n) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B)), 1);
        pen.Freeze();
        var max = Math.Max(1m, rows.Max(r => r.Total));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            for (var i = n - 1; i < rows.Count; i++)
            {
                var ma = rows.Skip(i + 1 - n).Take(n).Average(r => r.Total);
                var x = (i + 0.5) * w / rows.Count;
                // 与柱状同一比例尺；顶部留 8px 给均线余量
                var y = h - (double)(ma / max) * (h - 20) - 8;
                if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
