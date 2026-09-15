using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace StockWidget.App.Views;

/// <summary>
/// 数字输入框（带上下调节器）：▲▼ 按钮长按连发、滚轮与 ↑↓ 键均可步进。
/// <see cref="Text"/> 直通内部输入框文本，便于替换普通 TextBox 使用。
/// </summary>
public sealed class SpinnerBox : UserControl
{
    private readonly TextBox _box;

    /// <summary>步进下限。</summary>
    public int Min { get; set; } = 1;

    /// <summary>步进上限。</summary>
    public int Max { get; set; } = 999;

    /// <summary>单次步进量。</summary>
    public int Step { get; set; } = 1;

    /// <summary>值变化（含手动输入与按钮/滚轮/键盘步进）。</summary>
    public event EventHandler? ValueChanged;

    public SpinnerBox()
    {
        _box = new TextBox
        {
            TextAlignment = TextAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2, 2, 16, 2),
        };

        var up = new RepeatButton { Content = "▲", Width = 13, Height = 11, FontSize = 7, Delay = 400, Interval = 60, Focusable = false };
        var down = new RepeatButton { Content = "▼", Width = 13, Height = 11, FontSize = 7, Delay = 400, Interval = 60, Focusable = false };
        up.Click += (_, _) => StepValue(+Step);
        down.Click += (_, _) => StepValue(-Step);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 2, 1),
        };
        buttons.Children.Add(up);
        buttons.Children.Add(down);

        var grid = new Grid();
        grid.Children.Add(_box);
        grid.Children.Add(buttons);
        Content = grid;

        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { StepValue(+Step); e.Handled = true; }
            else if (e.Key == Key.Down) { StepValue(-Step); e.Handled = true; }
        };
        _box.PreviewMouseWheel += (_, e) =>
        {
            StepValue(e.Delta > 0 ? +Step : -Step);
            e.Handled = true;
        };
    }

    /// <summary>文本直通（兼容调用方按文本读取 / 写入）。</summary>
    public string Text
    {
        get => _box.Text;
        set => _box.Text = value;
    }

    private void StepValue(int delta)
    {
        if (!int.TryParse(_box.Text, out var v)) v = Min;
        var next = Math.Clamp(v + delta, Min, Max);
        if (next == v) return;
        _box.Text = next.ToString();
        _box.CaretIndex = _box.Text.Length;
    }
}
