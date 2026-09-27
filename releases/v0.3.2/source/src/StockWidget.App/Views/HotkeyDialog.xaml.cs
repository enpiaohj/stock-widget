using System.Windows;
using System.Windows.Input;
using StockWidget.Core.Services;

namespace StockWidget.App.Views;

/// <summary>
/// 修改全局热键对话框：捕获式录入（按键即填入），保存时校验。
/// </summary>
public partial class HotkeyDialog : Window
{
    /// <summary>保存成功后的热键组合；取消为 null。</summary>
    public string? Result { get; private set; }

    public HotkeyDialog(string current)
    {
        InitializeComponent();
        HotkeyBox.Text = HotkeyParser.NormalizeDisplay(current);
        Loaded += (_, _) => HotkeyBox.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is Key.Escape && HotkeyBox.IsKeyboardFocused)
        {
            // Esc 仅在无修饰键时退出焦点，不当作热键
            if (Keyboard.Modifiers == ModifierKeys.None)
                return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        var parts = new System.Collections.Generic.List<string>();
        var mod = Keyboard.Modifiers;
        if (mod.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (mod.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (mod.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (mod.HasFlag(ModifierKeys.Windows)) parts.Add("win");

        var keyText = key switch
        {
            >= Key.F1 and <= Key.F12 => key.ToString().ToLowerInvariant(),
            Key.Space => "space",
            Key.Oem3 => "`",
            _ when key is >= Key.A and <= Key.Z => key.ToString().ToLowerInvariant(),
            _ when key is >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            _ when key is >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
            _ => null,
        };
        if (keyText is null || parts.Count == 0) return;

        parts.Add(keyText);
        HotkeyBox.Text = string.Join("+", parts);
        e.Handled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var combo = HotkeyBox.Text.Trim();
        if (!HotkeyParser.TryParse(combo, out _, out _))
        {
            MessageBox.Show(this, "热键格式无效：需要修饰键 + 主键，如 ctrl+q。", "修改快捷键",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = HotkeyParser.NormalizeDisplay(combo);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
