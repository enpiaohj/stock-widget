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
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;
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

    // AI Key 状态机：未输入新 Key 时保留原密文；点击"清空"才明确删除
    private bool _aiKeyModified;
    private bool _aiKeyCleared;
    private bool _aiKeyRevealed;
    private readonly string? _initialSection;

    /// <summary>设置是否被保存 / 应用过（窗口侧据此刷新菜单等）。</summary>
    public bool Saved => _saved;

    private sealed record AlertRuleRow(long Id, string DisplayName, string DirectionText, bool Enabled)
    {
        public bool Enabled { get; set; } = Enabled;
    }

    private readonly ObservableCollection<AlertRuleRow> _alertRows = [];

    public SettingsWindow(MainViewModel vm, string? initialSection = null)
    {
        _vm = vm;
        _original = vm.Settings.Clone();
        _working = _original.Clone();
        _initialSection = initialSection;
        InitializeComponent();

        // 取消时的主题回退基准（修复旧版"取消未恢复主题"缺陷：此处按 original 恢复）
        Closing += (_, _) =>
        {
            if (!_saved)
            {
                ThemeManager.Instance.Apply(_original.Theme);
                ThemeManager.Instance.ApplyAccent(_original.AccentColor);
                ThemeManager.Instance.ApplyCategoryColors(_original.CategoryColors);
            }
        };

        Loaded += (_, _) =>
        {
            LoadFromSettings();
            // AI 面板"前往设置"定位到 AI 分析页签
            if (_initialSection == "ai") Tabs.SelectedItem = AiTabItem;
        };
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

        // ---- AI 分析 ----
        AiEnabledCheck.IsChecked = _working.Ai.Enabled;
        AiBaseUrlBox.Text = _working.Ai.BaseUrl;
        AiModelBox.Text = _working.Ai.Model;
        AiTimeoutBox.Text = _working.Ai.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _aiKeyModified = false;
        _aiKeyCleared = false;
        AiKeyBox.Clear();
        RefreshAiKeyState();

        // 强调色选择
        BuildAccentPicker();

        // 分组颜色选择
        BuildCategoryColorPicker();

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

    /// <summary>市场分类 key 与显示名的对应（与 AppSettings.CategoryColors 的键一致）。</summary>
    private static readonly (string Key, string Label)[] CategoryColorRows =
    [
        ("index", "沪深指数"),
        ("etf", "ETF基金"),
        ("hongkong", "香港股票"),
        ("usstock", "美国股票"),
        ("stock", "沪深个股"),
    ];

    /// <summary>在「通用」标签内构建分组颜色选择区：每行一个分类 + 预设色板单选。</summary>
    private void BuildCategoryColorPicker()
    {
        CategoryColorPanel.Children.Clear();
        foreach (var (catKey, label) in CategoryColorRows)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            var labelBlock = new TextBlock
            {
                Text = label,
                Width = 68,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, "FgBrush");
            row.Children.Add(labelBlock);

            _working.CategoryColors.TryGetValue(catKey, out var selected);
            foreach (var pal in AccentPalette.All)
            {
                var rb = new System.Windows.Controls.RadioButton
                {
                    GroupName = $"Category_{catKey}",
                    Tag = pal.Key,
                    IsChecked = string.Equals(selected, pal.Key, StringComparison.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 0, 10, 0),
                    ToolTip = $"{label}：{pal.Name}",
                    Content = new Border
                    {
                        Width = 20,
                        Height = 20,
                        CornerRadius = new CornerRadius(5),
                        Background = AccentRgbBrush(pal.AccentRgb),
                    },
                };
                rb.Checked += (_, _) =>
                {
                    _working.CategoryColors[catKey] = (string)rb.Tag;
                    ThemeManager.Instance.ApplyCategoryColors(_working.CategoryColors); // 实时预览
                };
                row.Children.Add(rb);
            }
            CategoryColorPanel.Children.Add(row);
        }
    }

    private void ResetCategoryColors_Click(object sender, RoutedEventArgs e)
    {
        _working.CategoryColors.Clear();
        ThemeManager.Instance.ApplyCategoryColors(_working.CategoryColors);
        BuildCategoryColorPicker();
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
                    Width = 80,
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
        App.WriteCrashLog("Diag", new Exception($"滑块 ValueChanged: {e.NewValue}"));
        if (OpacityBox is null) return;
        var v = (int)Math.Round(e.NewValue);
        OpacityBox.Text = v.ToString(CultureInfo.InvariantCulture);
        PreviewOpacity(v);
    }

    private void OpacityBox_ValueChanged(object sender, EventArgs e)
    {
        App.WriteCrashLog("Diag", new Exception($"输入框 ValueChanged: {OpacityBox.Text}"));
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
        App.WriteCrashLog("Diag", new Exception($"PreviewOpacity: {percent}"));
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

        // AI 分析：Key 状态机——未输入新 Key 保留原密文；点过"清空"才删除
        if (!int.TryParse(AiTimeoutBox.Text, out var aiTimeout) || aiTimeout is < 5 or > 300)
        {
            error = "AI 分析超时需为 5-300 秒。";
            return false;
        }
        cfg.Ai = new AiSettings
        {
            Enabled = AiEnabledCheck.IsChecked == true,
            BaseUrl = AiBaseUrlBox.Text.Trim(),
            Model = AiModelBox.Text.Trim(),
            TimeoutSeconds = aiTimeout,
            ApiKeyEncrypted = _aiKeyCleared
                ? ""
                : _aiKeyModified && CurrentKeyInput().Length > 0
                    ? AiCredentialProtector.Protect(CurrentKeyInput())
                    : _working.Ai.ApiKeyEncrypted,
        };

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

    // ---------------------------
    // AI 分析配置（DeepSeek）
    // ---------------------------

    /// <summary>刷新 Key 状态提示（不显示明文/完整密钥）。</summary>
    private void RefreshAiKeyState()
    {
        var configured = _aiKeyModified && CurrentKeyInput().Length > 0;
        if (_aiKeyCleared)
            AiStatusText.Text = "API Key 已清除，保存后生效";
        else if (configured)
            AiStatusText.Text = "已输入新 Key，保存后生效";
        else
            AiStatusText.Text = !string.IsNullOrEmpty(_working.Ai.ApiKeyEncrypted)
                ? "已配置（输入新 Key 可覆盖）"
                : "未配置";
    }

    private void AiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _aiKeyModified = true;
        RefreshAiKeyState();
    }

    private void AiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_aiKeyRevealed)
        {
            _aiKeyModified = true;
            RefreshAiKeyState();
        }
    }

    /// <summary>眼睛切换：明文查看 API Key（检查是否误输入空格等）。</summary>
    private void AiKeyEye_Click(object sender, RoutedEventArgs e)
    {
        _aiKeyRevealed = !_aiKeyRevealed;
        if (_aiKeyRevealed)
        {
            AiKeyTextBox.Text = AiKeyBox.Password;
            AiKeyBox.Visibility = Visibility.Collapsed;
            AiKeyTextBox.Visibility = Visibility.Visible;
        }
        else
        {
            AiKeyBox.Password = AiKeyTextBox.Text;
            AiKeyTextBox.Visibility = Visibility.Collapsed;
            AiKeyBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>当前 Key 输入（眼睛明文态取 TextBox，否则取 PasswordBox）。</summary>
    private string CurrentKeyInput() => _aiKeyRevealed ? AiKeyTextBox.Text : AiKeyBox.Password;

    /// <summary>拉取可用模型列表填充下拉框。</summary>
    private async void AiFetchModels_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = AiBaseUrlBox.Text.Trim();
        var key = _aiKeyModified && CurrentKeyInput().Length > 0
            ? CurrentKeyInput()
            : _aiKeyCleared ? "" : AiCredentialProtector.Unprotect(_working.Ai.ApiKeyEncrypted);

        if (string.IsNullOrWhiteSpace(baseUrl)) { AiStatusText.Text = "✗ 请先填写 API Base URL"; return; }
        if (string.IsNullOrWhiteSpace(key)) { AiStatusText.Text = "✗ 请先填写 API Key"; return; }

        AiFetchModelsButton.IsEnabled = false;
        AiStatusText.Text = "正在获取模型...";
        try
        {
            int.TryParse(AiTimeoutBox.Text, out var timeout);
            if (timeout is < 5 or > 300) timeout = 60;
            using var client = new DeepSeekAiClient();
            var models = await client.GetModelsAsync(baseUrl, key, timeout, CancellationToken.None);
            AiModelBox.ItemsSource = models;
            AiStatusText.Text = models.Count > 0
                ? $"✓ 获取到 {models.Count} 个模型，可下拉选择"
                : "✗ 服务未返回任何模型";
        }
        catch (AiApiException ex)
        {
            AiStatusText.Text = $"✗ {ex.Message}";
        }
        catch (Exception ex)
        {
            AiStatusText.Text = $"✗ 获取失败：{ex.Message}";
        }
        finally
        {
            AiFetchModelsButton.IsEnabled = true;
        }
    }

    private void AiKeyClear_Click(object sender, RoutedEventArgs e)
    {
        AiKeyBox.Clear();
        AiKeyTextBox.Clear();
        _aiKeyModified = false;
        _aiKeyCleared = true;
        RefreshAiKeyState();
    }

    /// <summary>最小请求验证 Base URL / Key / Model / 网络（不落日志明文）。</summary>
    private async void AiTest_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = AiBaseUrlBox.Text.Trim();
        var model = AiModelBox.Text.Trim();
        var key = _aiKeyModified && CurrentKeyInput().Length > 0
            ? CurrentKeyInput()
            : _aiKeyCleared ? "" : AiCredentialProtector.Unprotect(_working.Ai.ApiKeyEncrypted);

        if (string.IsNullOrWhiteSpace(baseUrl)) { AiStatusText.Text = "✗ 请先填写 API Base URL"; return; }
        if (string.IsNullOrWhiteSpace(model)) { AiStatusText.Text = "✗ 请先填写 Model"; return; }
        if (string.IsNullOrWhiteSpace(key)) { AiStatusText.Text = "✗ 请先填写 API Key"; return; }

        int.TryParse(AiTimeoutBox.Text, out var timeout);
        if (timeout is < 5 or > 300) timeout = 60;

        AiTestButton.IsEnabled = false;
        AiStatusText.Text = "正在测试...";
        try
        {
            using var client = new DeepSeekAiClient();
            await client.CompleteAsync(baseUrl, key, model,
                "你是连接测试助手。", "连接测试：请仅返回 JSON {\"ok\":true}。", timeout, CancellationToken.None);
            AiStatusText.Text = "✓ DeepSeek API 连接正常";
        }
        catch (AiApiException ex)
        {
            AiStatusText.Text = $"✗ {ex.Message}";
        }
        catch (Exception ex)
        {
            AiStatusText.Text = $"✗ 连接失败：{ex.Message}";
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
    }

    private void ResetFields_Click(object sender, RoutedEventArgs e)
    {
        foreach (var panel in OptionalPanel.Children.OfType<StackPanel>())
        {
            if (panel.Children[0] is CheckBox cb)
                cb.IsChecked = false;
        }
    }
}
