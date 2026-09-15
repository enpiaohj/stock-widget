using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StockWidget.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using StockWidget.App.Services;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Data;
using StockWidget.Core.Models;
using StockWidget.Core.Services;
using MenuItem = System.Windows.Controls.MenuItem;

namespace StockWidget.App.Views;

/// <summary>设置中心：显示设置 / 显示字段 / 快捷键 / 预警 / 通用。</summary>
public partial class SettingsWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private readonly AppSettings _original;
    private AppSettings _working;
    private bool _capturing;
    private bool _saved;

    /// <summary>设置是否被保存 / 应用过（窗口侧据此刷新菜单等）。</summary>
    public bool Saved => _saved;

    private sealed record AlertRuleRow(long Id, string DisplayName, string DirectionText, bool Enabled)
    {
        public bool Enabled { get; set; } = Enabled;
    }

    private readonly ObservableCollection<AlertRuleRow> _alertRows = [];

    public SettingsWindow(MainViewModel vm)
    {
        _vm = vm;
        _original = vm.Settings.Clone();
        _working = _original.Clone();
        InitializeComponent();

        // 取消时的主题回退基准（修复旧版"取消未恢复主题"缺陷：此处按 original 恢复）
        Closing += (_, _) =>
        {
            if (!_saved)
            {
                ThemeManager.Instance.Apply(_original.Theme);
                ThemeManager.Instance.ApplyAccent(_original.AccentColor);
            }
        };

        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        // ---- 显示设置 ----
        Select(ThemeBox, _working.Theme);
        OpacitySlider.Value = _working.OpacityPercent;
        OpacityBox.Text = _working.OpacityPercent.ToString(CultureInfo.InvariantCulture);

        var fonts = System.Windows.Media.Fonts.SystemFontFamilies
            .OrderBy(f => f.ToString());
        foreach (var f in fonts)
            FontBox.Items.Add(f.ToString());
        if (!fonts.Any(f => f.ToString() == _working.FontFamily))
            FontBox.Items.Add(_working.FontFamily);
        FontBox.SelectedItem = _working.FontFamily;
        FontSizeBox.Text = _working.FontSize.ToString(CultureInfo.InvariantCulture);
        ItalicCheck.IsChecked = _working.FontItalic;

        IntervalBox.Text = (_working.RefreshIntervalMs / 1000).ToString(CultureInfo.InvariantCulture);
        ShowAmountCheck.IsChecked = _working.ShowTotalAmount;

        ShowDividersCheck.IsChecked = _working.ShowDividers;
        ShowIntervalCheck.IsChecked = _working.ShowRefreshInterval;
        ShowLockCheck.IsChecked = _working.ShowLockedStatus;
        ShowWeekdayCheck.IsChecked = _working.ShowUpdateWeekday;
        ShowWeekNumberCheck.IsChecked = _working.ShowUpdateWeekNumber;

        // ---- 显示字段 ----
        BuildFieldPanels();

        // ---- 快捷键 ----
        HotkeyBox.Text = HotkeyParser.NormalizeDisplay(_working.Hotkey);

        // ---- 预警 ----
        AlertsEnabledCheck.IsChecked = _working.AlertsEnabled;
        ReloadAlerts();

        // ---- 通用 ----
        AutoStartCheck.IsChecked = _working.AutoStart;
        GroupByCategoryCheck.IsChecked = _working.GroupByCategory;
        ShowSparklineCheck.IsChecked = _working.ShowSparkline;

        // 强调色选择
        BuildAccentPicker();

        DataDirText.Text = $"数据库位置：{DbPathResolver.GetDatabasePath()}";
    }

    /// <summary>根据 0xRRGGBB 生成强调色画笔。</summary>
    private static System.Windows.Media.Brush AccentRgbBrush(int rgb) =>
        new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF)));

    /// <summary>在「通用」标签内构建强调色单选项选择区。</summary>
    private void BuildAccentPicker()
    {
        AccentPanel.Children.Clear();
        foreach (var pal in AccentPalette.All)
        {
            var rb = new System.Windows.Controls.RadioButton
            {
                GroupName = "Accent",
                Tag = pal.Key,
                IsChecked = string.Equals(_working.AccentColor, pal.Key, StringComparison.OrdinalIgnoreCase),
                Margin = new Thickness(0, 0, 12, 6),
                ToolTip = pal.Name,
                Content = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(6),
                    Background = AccentRgbBrush(pal.AccentRgb),
                },
            };
            rb.Checked += (_, _) =>
            {
                _working.AccentColor = (string)rb.Tag;
                ThemeManager.Instance.ApplyAccent(_working.AccentColor); // 实时预览
            };
            AccentPanel.Children.Add(rb);
        }
    }

    private void BuildFieldPanels()
    {
        RequiredPanel.Children.Clear();
        OptionalPanel.Children.Clear();

        foreach (var def in FieldDefinitions.All)
        {
            if (def.Required)
            {
                var cb = new CheckBox
                {
                    Content = def.Display,
                    IsChecked = true,
                    IsEnabled = false,
                    Margin = new Thickness(0, 2, 16, 2),
                    ToolTip = $"字段：{def.Display}（{def.Key}，必选）",
                };
                RequiredPanel.Children.Add(cb);
            }
            else
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
                var cb = new CheckBox
                {
                    Content = def.Display,
                    IsChecked = _working.CustomFields.Contains(def.Key),
                    ToolTip = $"字段：{def.Display}（{def.Key}）",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var widthBox = new SpinnerBox
                {
                    Width = 52,
                    Min = 6,
                    Max = 40,
                    Text = (_working.FieldWidths.TryGetValue(def.Key, out var w) ? w : def.DefaultWidth)
                        .ToString(CultureInfo.InvariantCulture),
                    Margin = new Thickness(8, 0, 0, 0),
                    ToolTip = "列宽（字符单位 6-40，▲▼/滚轮可调）",
                };
                panel.Children.Add(cb);
                panel.Children.Add(widthBox);
                OptionalPanel.Children.Add(panel);
            }
        }
    }

    private static void Select(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((string?)item.Tag == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0;
    }

    // ---------------------------
    // 实时预览
    // ---------------------------

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
        {
            _working.Theme = theme;
            ThemeManager.Instance.Apply(theme); // 实时预览，取消时按 original 恢复
        }
    }

    private void Opacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityBox is null) return;
        var v = (int)Math.Round(e.NewValue);
        OpacityBox.Text = v.ToString(CultureInfo.InvariantCulture);
        PreviewOpacity(v);
    }

    private void OpacityBox_ValueChanged(object sender, EventArgs e)
    {
        if (OpacitySlider is null) return;
        if (int.TryParse(OpacityBox.Text, out var v) && v is >= 10 and <= 100)
        {
            if (Math.Abs(OpacitySlider.Value - v) > 0.5)
                OpacitySlider.Value = v;
            PreviewOpacity(v);
        }
    }

    private void PreviewOpacity(int percent)
    {
        // 实时预览与保存后同一机制：仅背景 alpha 变化，文字恒清晰
        ThemeManager.Instance.ApplyOpacity(percent);
    }

    // ---------------------------
    // 快捷键监听
    // ---------------------------

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        _capturing = !_capturing;
        CaptureButton.Content = _capturing ? "⏹ 停止监听" : "🎧 开始监听";
        HotkeyBox.IsReadOnly = _capturing;
        if (_capturing) HotkeyBox.Clear();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_capturing) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return; // 只等主键

        if (key is Key.Escape)
        {
            e.Handled = true;
            StopCapture();
            return;
        }

        var combo = BuildCombo(Keyboard.Modifiers, key);
        if (combo is not null)
        {
            HotkeyBox.Text = combo;
            e.Handled = true;
            StopCapture();
        }
    }

    private void StopCapture()
    {
        _capturing = false;
        CaptureButton.Content = "🎧 开始监听";
        HotkeyBox.IsReadOnly = false;
    }

    private static string? BuildCombo(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("win");

        var keyText = key switch
        {
            >= Key.F1 and <= Key.F12 => key.ToString().ToLowerInvariant(),
            Key.Space => "space",
            Key.Oem3 => "`",
            _ when key is >= Key.A and <= Key.Z => key.ToString().ToLowerInvariant(),
            _ when key is >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(CultureInfo.InvariantCulture),
            _ when key is >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
        if (keyText is null || parts.Count == 0) return null;
        parts.Add(keyText);
        return string.Join("+", parts);
    }

    // ---------------------------
    // 预警规则
    // ---------------------------

    private void ReloadAlerts()
    {
        _alertRows.Clear();
        try
        {
            var repo = App.Services.GetRequiredService(typeof(IAlertRepository)) as IAlertRepository;
            foreach (var rule in repo!.GetAll())
            {
                _alertRows.Add(new AlertRuleRow(
                    rule.Id,
                    $"{(rule.StockName.Length > 0 ? rule.StockName + " " : "")}{StockCodeNormalizer.StripPrefix(rule.Code)}",
                    rule.Direction switch
                    {
                        AlertDirection.RiseOnly => $"仅涨 ≥ {rule.ThresholdPct:0.##}%",
                        AlertDirection.FallOnly => $"仅跌 ≤ -{rule.ThresholdPct:0.##}%",
                        _ => $"|涨跌| ≥ {rule.ThresholdPct:0.##}%",
                    },
                    rule.Enabled));
            }
        }
        catch
        {
            // 加载失败保持空列表
        }
        AlertList.ItemsSource = _alertRows;
    }

    private void AddAlert_Click(object sender, RoutedEventArgs e)
    {
        var code = StockCodeNormalizer.Normalize(AlertCodeBox.Text);
        if (code.Length == 0)
        {
            MessageBox.Show(this, "请输入有效的股票代码。", "预警设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!decimal.TryParse(AlertThresholdBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
            || threshold is <= 0 or > 50)
        {
            MessageBox.Show(this, "阈值需为 0-50 之间的数字（涨跌幅百分比）。", "预警设置",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var repo = App.Services.GetRequiredService(typeof(IAlertRepository)) as IAlertRepository;
        if (repo!.ExistsForCode(code))
        {
            MessageBox.Show(this, "该股票已有预警规则。", "预警设置", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var direction = AlertDirectionBox.SelectedIndex switch
        {
            1 => AlertDirection.RiseOnly,
            2 => AlertDirection.FallOnly,
            _ => AlertDirection.Any,
        };
        repo.Add(code, "", threshold, direction);
        AlertCodeBox.Clear();
        AlertThresholdBox.Clear();
        ReloadAlerts();
    }

    private void DeleteAlert_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AlertRuleRow row) return;
        var repo = App.Services.GetRequiredService(typeof(IAlertRepository)) as IAlertRepository;
        repo!.Remove(row.Id);
        ReloadAlerts();
    }

    private void AlertEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AlertRuleRow row) return;
        var repo = App.Services.GetRequiredService(typeof(IAlertRepository)) as IAlertRepository;
        var rule = repo!.GetAll().FirstOrDefault(r => r.Id == row.Id);
        if (rule is null) return;
        rule.Enabled = row.Enabled;
        repo.Update(rule);
    }

    // ---------------------------
    // 通用
    // ---------------------------

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DbPathResolver.GetDataDirectory(),
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开失败忽略
        }
    }

    private async void ImportLegacy_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择旧版（Python）数据目录（含 stock_config.json / history_amount.json）",
        };
        if (dlg.ShowDialog(this) != true) return;

        var importer = App.Services.GetRequiredService(typeof(ILegacyImporter)) as ILegacyImporter;
        var result = importer!.ImportFromDirectory(dlg.FolderName);
        MessageBox.Show(this,
            result.HasAnything
                ? $"导入完成：自选股 {result.StocksImported} 只，历史成交额 {result.HistoryDays} 天。重启后完全生效。"
                : $"未导入任何数据。{result.Error}",
            "从旧版导入", MessageBoxButton.OK, MessageBoxImage.Information);

        if (result.StocksImported > 0)
        {
            // 重新加载自选股
            var vm = _vm;
            await vm.InitializeAsync();
        }
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出数据备份",
            Filter = "StockWidget 备份 (*.json)|*.json",
            FileName = $"StockWidget-数据备份-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dlg.ShowDialog(this) != true) return;

        var svc = App.Services.GetRequiredService(typeof(IBackupService)) as IBackupService;
        var result = await svc!.ExportAsync(dlg.FileName);
        MessageBox.Show(this, result.Message, "导出数据备份",
            MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private async void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入数据备份",
            Filter = "StockWidget 备份 (*.json)|*.json",
        };
        if (dlg.ShowDialog(this) != true) return;

        var svc = App.Services.GetRequiredService(typeof(IBackupService)) as IBackupService;
        var result = await svc!.ImportAsync(dlg.FileName);
        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "导入数据备份", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(this, result.Message, "导入数据备份", MessageBoxButton.OK, MessageBoxImage.Information);
        // 重新加载自选股（与"从旧版导入"一致）；设置在重启后完全生效
        await _vm.InitializeAsync();
    }

    // ---------------------------
    // 收集与应用
    // ---------------------------

    private bool TryCollect(out AppSettings cfg, out string error)
    {
        cfg = _working.Clone();
        error = "";

        if (!int.TryParse(FontSizeBox.Text, out var fontSize) || fontSize is < 6 or > 28)
        {
            error = "字号需为 6-28。";
            return false;
        }
        if (!int.TryParse(IntervalBox.Text, out var intervalSec) || intervalSec is < 1 or > 120)
        {
            error = "刷新间隔需为 1-120 秒。";
            return false;
        }
        if (!int.TryParse(OpacityBox.Text, out var opacity) || opacity is < 10 or > 100)
        {
            error = "透明度需为 10-100。";
            return false;
        }
        if (!HotkeyParser.TryParse(HotkeyBox.Text.Trim(), out _, out _))
        {
            error = "热键格式无效：需要修饰键 + 主键，如 ctrl+q。";
            return false;
        }

        cfg.FontSize = fontSize;
        cfg.FontFamily = FontBox.SelectedItem as string ?? cfg.FontFamily;
        cfg.FontItalic = ItalicCheck.IsChecked == true;
        cfg.OpacityPercent = opacity;
        cfg.RefreshIntervalMs = intervalSec * 1000;
        cfg.ShowTotalAmount = ShowAmountCheck.IsChecked == true;
        cfg.ShowDividers = ShowDividersCheck.IsChecked == true;
        cfg.ShowRefreshInterval = ShowIntervalCheck.IsChecked == true;
        cfg.ShowLockedStatus = ShowLockCheck.IsChecked == true;
        cfg.ShowUpdateWeekday = ShowWeekdayCheck.IsChecked == true;
        cfg.ShowUpdateWeekNumber = ShowWeekNumberCheck.IsChecked == true;
        cfg.Hotkey = HotkeyParser.NormalizeDisplay(HotkeyBox.Text.Trim());
        cfg.AlertsEnabled = AlertsEnabledCheck.IsChecked == true;
        cfg.AutoStart = AutoStartCheck.IsChecked == true;
        cfg.GroupByCategory = GroupByCategoryCheck.IsChecked == true;
        cfg.ShowSparkline = ShowSparklineCheck.IsChecked == true;
        cfg.AccentColor = _working.AccentColor;

        // 字段
        cfg.CustomFields.Clear();
        var children = OptionalPanel.Children.OfType<StackPanel>().ToList();
        var defs = FieldDefinitions.All.Where(f => !f.Required).ToList();
        for (var i = 0; i < children.Count && i < defs.Count; i++)
        {
            var cb = (CheckBox)children[i].Children[0];
            var widthBox = (SpinnerBox)children[i].Children[1]; // 列宽框已改为 SpinnerBox
            if (cb.IsChecked == true)
                cfg.CustomFields.Add(defs[i].Key);
            if (int.TryParse(widthBox.Text, out var w))
                cfg.FieldWidths[defs[i].Key] = Math.Clamp(w, 6, 40);
        }

        return true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCollect(out var cfg, out var error))
        {
            MessageBox.Show(this, error, "设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _working = cfg.Clone();
        _vm.ApplySettings(cfg);
        ((MainWindow)Owner)?.ApplyAppearance();
        _saved = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Apply_Click(sender, e);
        if (_saved) Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetFields_Click(object sender, RoutedEventArgs e)
    {
        foreach (var panel in OptionalPanel.Children.OfType<StackPanel>())
        {
            if (panel.Children[0] is CheckBox cb)
                cb.IsChecked = false;
        }
    }
}
