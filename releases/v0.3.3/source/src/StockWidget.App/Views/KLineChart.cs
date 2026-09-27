using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StockWidget.Core.Data.Entities;

namespace StockWidget.App.Views;

/// <summary>
/// 日K蜡烛图：最近 N 根蜡烛（红涨绿跌）+ 底部成交量副图（1/4 高）+ MA5/10/20 均线
/// + 可见区间最高/最低点价格标注 + 鼠标悬停柱定位虚线与开高低收数据面板。
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

    /// <summary>悬停聚焦的蜡烛索引（相对可见区间）；-1 = 无悬停。</summary>
    private int _hoverIndex = -1;

    /// <summary>悬停鼠标位置（跟手面板定位）。</summary>
    private Point _hoverPos;

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
        // 价格区上下各预留 24px 给最高/最低价标注（同花顺做法），避免极值文字溢出控件顶部
        // 与区间涨跌文字重叠；maxP 端点 y=24，minP 端点 y=priceH-34
        const double Pad = 24;
        double Y(decimal p) => Pad + (priceH - 2 * Pad - 10) * (double)(1 - (p - minP) / (maxP - minP));
        double X(int i) => (i + 0.5) * barW;

        // 均线（画在蜡烛下层）：MA5 白 / MA10 金 / MA20 紫
        DrawMa(dc, bars, 5, w, priceH, maxP, minP, Colors.White);
        DrawMa(dc, bars, 10, w, priceH, maxP, minP, Colors.Gold);
        DrawMa(dc, bars, 20, w, priceH, maxP, minP, Colors.MediumPurple);

        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var xC = X(i);
            var up = b.Close >= b.Open;
            var brush = T(up ? "UpBrush" : "DownBrush");

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

        // 可见区间最高 / 最低点价格标注（同花顺风格：极值端点旁标价 + 指向箭头）
        var axisBrush = T("SubFgBrush");
        var hiIdx = 0;
        var loIdx = 0;
        for (var i = 1; i < bars.Count; i++)
        {
            if (bars[i].High >= bars[hiIdx].High) hiIdx = i; // 并列取最右，避免文字贴左边缘
            if (bars[i].Low <= bars[loIdx].Low) loIdx = i;
        }
        DrawPriceTag(dc, bars[hiIdx].High.ToString("0.##", CultureInfo.CurrentCulture),
            X(hiIdx), Y(bars[hiIdx].High), w, pointDown: true);
        DrawPriceTag(dc, bars[loIdx].Low.ToString("0.##", CultureInfo.CurrentCulture),
            X(loIdx), Y(bars[loIdx].Low), w, pointDown: false);

        // 右侧价格轴：区间最高 / 最低（与极值端点同高）
        DrawLabel(dc, maxP.ToString("0.##", CultureInfo.CurrentCulture), w - 2, Pad - 9,
            TextAlignment.Right, axisBrush);
        DrawLabel(dc, minP.ToString("0.##", CultureInfo.CurrentCulture), w - 2, priceH - 34 - 9,
            TextAlignment.Right, axisBrush);

        // 底部日期轴：首根 / 末根日期
        DrawLabel(dc, bars[0].Date, 0, h - 13, TextAlignment.Left, axisBrush);
        DrawLabel(dc, bars[^1].Date, w, h - 13, TextAlignment.Right, axisBrush);

        // 左上 MA 值标注（末根均线值）：MA5 白 / MA10 金 / MA20 紫
        var labelY = 2;
        if (bars.Count >= 5)
        {
            var ma5 = bars.TakeLast(5).Average(b => b.Close);
            DrawLabel(dc, $"MA5:{ma5:0.00}", 2, labelY, TextAlignment.Left, Brushes.White);
            labelY += 14;
        }
        if (bars.Count >= 10)
        {
            var ma10 = bars.TakeLast(10).Average(b => b.Close);
            DrawLabel(dc, $"MA10:{ma10:0.00}", 2, labelY, TextAlignment.Left, Brushes.Gold);
            labelY += 14;
        }
        if (bars.Count >= 20)
        {
            var ma20 = bars.TakeLast(20).Average(b => b.Close);
            DrawLabel(dc, $"MA20:{ma20:0.00}", 2, labelY, TextAlignment.Left, Brushes.MediumPurple);
        }

        // 悬停：十字光标（横竖虚线）+ 右侧价格标签 + 跟手数据面板
        if (_hoverIndex >= 0 && _hoverIndex < bars.Count)
        {
            var dashPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1)
            { DashStyle = DashStyles.Dash };
            dashPen.Freeze();
            var hy = _hoverPos.Y;
            dc.DrawLine(dashPen, new Point(0, hy), new Point(w, hy));

            // 横线在价格区内时，右侧轴显示对应价格（同花顺十字光标样式）
            if (hy >= Pad && hy <= priceH - 34)
            {
                var hp = minP + (decimal)(1 - (hy - Pad) / (priceH - 2 * Pad - 10)) * (maxP - minP);
                var txt = hp.ToString("0.##", CultureInfo.CurrentCulture);
                var ftP = new FormattedText(txt, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("微软雅黑"), 9, T("FgBrush"), 1.25);
                var tagW = ftP.Width + 10;
                var tagRect = new Rect(w - tagW - 1, hy - 8, tagW, 16);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x20)),
                    new Pen(T("SubFgBrush"), 0.5), tagRect);
                dc.DrawText(ftP, new Point(tagRect.X + 5, hy - 8 + 2));
            }

            DrawHoverOverlay(dc, all, bars, _hoverIndex, barW, w, h, priceH, _hoverPos);
        }
    }

    /// <summary>极值价格标注：文字居中于蜡烛（钳制在控件内）+ 指向影线端点的三角箭头。</summary>
    private static void DrawPriceTag(DrawingContext dc, string text, double xCenter, double yTip,
        double w, bool pointDown)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("微软雅黑"), 9, T("SubFgBrush"), 1.25);
        var textY = pointDown ? yTip - 22 : yTip + 10;
        var x = Math.Clamp(xCenter - ft.Width / 2, 1, Math.Max(1, w - ft.Width - 1));
        dc.DrawText(ft, new Point(x, textY));

        // 三角箭头：顶点朝向蜡烛影线端点
        var cx = Math.Clamp(xCenter, ft.Width / 2 + 1, Math.Max(ft.Width / 2 + 1, w - ft.Width / 2 - 1));
        var brush = T("SubFgBrush");
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            if (pointDown)
            {
                ctx.BeginFigure(new Point(cx - 4, yTip - 8), true, true);
                ctx.LineTo(new Point(cx + 4, yTip - 8), true, false);
                ctx.LineTo(new Point(cx, yTip - 2), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(cx - 4, yTip + 8), true, true);
                ctx.LineTo(new Point(cx + 4, yTip + 8), true, false);
                ctx.LineTo(new Point(cx, yTip + 2), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }

    private static void DrawHoverOverlay(DrawingContext dc, IReadOnlyList<DailyKlineEntity> all,
        List<DailyKlineEntity> bars, int idx, double barW, double w, double h, double priceH, Point pos)
    {
        var b = bars[idx];
        var xC = (idx + 0.5) * barW;

        // 柱定位竖虚线（贯穿价格区 + 量区）
        var dashPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), 1)
        { DashStyle = DashStyles.Dash };
        dashPen.Freeze();
        dc.DrawLine(dashPen, new Point(xC, 0), new Point(xC, h - 13));

        // 涨跌幅：前一根收盘为基准（可见首根取全量序列的前一根，无则用开盘价）
        var firstVisible = all.Count - bars.Count;
        var prevClose = idx > 0 ? bars[idx - 1].Close
            : firstVisible > 0 ? all[firstVisible - 1].Close : b.Open;
        var pct = prevClose != 0 ? (b.Close - prevClose) / prevClose * 100 : 0m;
        var pctText = $"{(pct >= 0 ? "+" : "")}{pct:0.00}%";
        var pctBrush = T(pct > 0 ? "UpBrush" : pct < 0 ? "DownBrush" : "FlatBrush");

        string[] lines =
        [
            b.Date,
            $"开 {b.Open:0.##}  高 {b.High:0.##}",
            $"低 {b.Low:0.##}  收 {b.Close:0.##}",
            $"涨跌 {pctText}  量 {b.Volume:N0}",
        ];
        if (b.Amount > 0)
            lines[3] = $"涨跌 {pctText}  额 {b.Amount:0}";
        const double panelW = 168;
        var panelH = 14 * lines.Length + 6;

        // 跟手定位：默认悬停点右上，右缘翻转到左侧、顶部翻转到下方（避开窗口按钮与价格轴）
        var px = pos.X + 14;
        if (px + panelW > w - 2) px = pos.X - panelW - 14;
        var py = pos.Y - panelH - 12;
        if (py < 2) py = pos.Y + 16;
        px = Math.Clamp(px, 2, Math.Max(2, w - panelW - 2));
        var panelRect = new Rect(px, py, panelW, panelH);
        // 近乎不透明：卡片本身半透明，面板若太透会与透出的桌面内容（其他行情软件）混字
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xF2, 0x18, 0x18, 0x20)), null, panelRect);

        var y = panelRect.Y + 3;
        for (var i = 0; i < lines.Length; i++)
        {
            var brush = i == 3 ? pctBrush : T("FgBrush");
            var ft = new FormattedText(lines[i], CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("微软雅黑"), 9.5, brush, 1.25);
            dc.DrawText(ft, new Point(panelRect.X + 6, y));
            y += 14;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var all = Klines;
        if (all is not { Count: > 1 } || ActualWidth <= 0) return;

        var bars = all.TakeLast(Math.Min(VisibleBars, all.Count)).ToList();
        var barW = ActualWidth / bars.Count;
        var pos = e.GetPosition(this);
        var idx = (int)(pos.X / barW);
        if (idx < 0 || idx >= bars.Count) idx = -1;

        if (idx != _hoverIndex || (idx >= 0 && _hoverPos != pos))
        {
            _hoverPos = pos;
            _hoverIndex = idx;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
    }

    private static void DrawLabel(DrawingContext dc, string text, double x, double y,
        TextAlignment align, Brush brush)
    {
        if (string.IsNullOrEmpty(text)) return;
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("微软雅黑"), 9, brush, 1.25);
        ft.TextAlignment = align;
        dc.DrawText(ft, new Point(x, y));
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
