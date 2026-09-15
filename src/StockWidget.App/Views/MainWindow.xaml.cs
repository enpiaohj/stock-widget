using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using StockWidget.App.Services;
using StockWidget.App.ViewModels;
using StockWidget.Core.Models;
using StockWidget.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace StockWidget.App.Views;

/// <summary>
/// 主悬浮窗：行情表 + 底部量能栏；无边框、置顶、透明度、拖拽、锁定、
/// 全局热键显隐、系统托盘联动。
/// </summary>
public partial class MainWindow : GlassWindow
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _posSaveTimer;
    private readonly ToolTip _tip = new() { StaysOpen = false, Placement = PlacementMode.MousePoint };
    private const int HotkeyId = 0xA001;
    private bool _animatingVisibility;
    private const double FadeDurationMs = 120;
    private HwndSource? _hwndSource;
    private MinuteChartWindow? _minuteWindow;

    // 供 XAML 绑定的行字体（避免与 Window 自带属性重名）
    public static readonly DependencyProperty RowFontFamilyProperty = DependencyProperty.Register(
        nameof(RowFontFamily), typeof(FontFamily), typeof(MainWindow),
        new PropertyMetadata(new FontFamily("微软雅黑")));
    public static readonly DependencyProperty RowFontSizeProperty = DependencyProperty.Register(
        nameof(RowFontSize), typeof(double), typeof(MainWindow), new PropertyMetadata(12.0));
    public static readonly DependencyProperty RowFontStyleProperty = DependencyProperty.Register(
        nameof(RowFontStyle), typeof(FontStyle), typeof(MainWindow),
        new PropertyMetadata(FontStyles.Normal));

    public FontFamily RowFontFamily { get => (FontFamily)GetValue(RowFontFamilyProperty); set => SetValue(RowFontFamilyProperty, value); }
    public double RowFontSize { get => (double)GetValue(RowFontSizeProperty); set => SetValue(RowFontSizeProperty, value); }
    public FontStyle RowFontStyle { get => (FontStyle)GetValue(RowFontStyleProperty); set => SetValue(RowFontStyleProperty, value); }

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = _vm;
        Topmost = true;

        _posSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _posSaveTimer.Tick += (_, _) =>
        {
            _posSaveTimer.Stop();
            _vm.SaveWindowPosition(Left, Top);
        };
        LocationChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal && IsLoaded)
            {
                _posSaveTimer.Stop();
                _posSaveTimer.Start();
            }
        };

        _vm.ColumnsChanged += RebuildColumns;
        // 右键哪行就选中哪行（右键菜单"调整顺序/删除"直接作用于所点行）
        Grid.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(Grid, e.OriginalSource as DependencyObject) is DataGridRow row)
                Grid.SelectedItem = row.DataContext;
        };
        // 行集合变化（初始化 / 导入 / 增删股票）后按内容重算窗口尺寸
        _vm.Rows.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(AutoSizeWindow, DispatcherPriority.Loaded);
        _vm.SettingsRequested += OpenSettings;
        _vm.AboutRequested += ShowAbout;
        _vm.QuitRequested += QuitApp;
        _vm.HotkeyChanged += RegisterHotkey;
        _vm.VisibilityToggleRequested += ToggleVisibility;
        // 分时窗口单实例：已打开则复用并切换股票，不再越开越多
        _vm.MinuteRequested += row =>
        {
            if (_minuteWindow is { IsLoaded: true } w)
            {
                w.ShowFor(row);
                return;
            }
            _minuteWindow = new MinuteChartWindow(_vm, row) { Owner = this };
            _minuteWindow.Show();
        };
        _vm.AlertsTriggered += alerts =>
            ToastService.ShowAlerts(alerts);

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.StatusText)
                or nameof(MainViewModel.TotalAmountText)
                or nameof(MainViewModel.AmountDiffText)
                or nameof(MainViewModel.YesterdaySummary))
            {
                RebuildAmountText();
            }
            if (e.PropertyName is nameof(MainViewModel.StatusText) or nameof(MainViewModel.Settings))
                LockBadge.Visibility = _vm.Settings.ShowLockedStatus && _vm.Settings.Locked
                    ? Visibility.Visible : Visibility.Collapsed;
            if (e.PropertyName == nameof(MainViewModel.MarketClosed))
                MarketClosedBadge.Visibility = _vm.MarketClosed ? Visibility.Visible : Visibility.Collapsed;
            if (e.PropertyName == nameof(MainViewModel.MarketMoodPct))
                ((App)Application.Current).UpdateTrayMood(_vm.MarketMoodPct);
        };

        SourceInitialized += (_, _) => HookHotkey();
        Loaded += async (_, _) =>
        {
            RebuildColumns();
            RebuildAmountText();
            BuildContextMenu();
            await _vm.InitializeAsync();
            ApplyAppearance();
            _vm.StartRefreshLoop();
        };
    }

    /// <summary>
    /// 为主卡片附加柔和投影；DropShadowEffect 依赖 GPU 位图合成，在无 GPU / 远程会话等
    /// 环境可能创建失败。此处容错：成功则附加，失败则静默回退（不阻断 UI 启动/渲染）。
    /// </summary>
    private void TryApplyWindowShadow()
    {
        try
        {
            var c = (Color)Application.Current.FindResource("CardShadowEffectBrush");
            var shadow = new DropShadowEffect
            {
                BlurRadius = 22,
                Direction = 270,
                ShadowDepth = 4,
                Opacity = 0.5,
                Color = c,
            };
            CardShadowHost.Effect = shadow;
        }
        catch (Exception)
        {
            // 阴影不可用则忽略：保留无阴影的细边框外观，保证应用正常启动
            CardShadowHost.Effect = null;
        }
    }

    // ---------------------------
    // 列构建
    // ---------------------------

    /// <summary>按配置重建表格列（必选字段 ∪ 自选字段 ∪ 迷你走势）。</summary>
    private void RebuildColumns()
    {
        var cfg = _vm.Settings;
        Grid.Columns.Clear();

        // 行间分隔虚线（行模板内自绘，GridLinesVisibility 在重模板化行上不渲染）
        RowDividerVisibility = cfg.ShowDividers ? Visibility.Visible : Visibility.Collapsed;

        var headerStyle = CreateHeaderStyle();

        foreach (var def in FieldDefinitions.All)
        {
            if (!def.Required && !cfg.CustomFields.Contains(def.Key)) continue;

            var col = BuildValueColumn(def);
            col.Width = FieldDefinitions.WidthToPixels(
                cfg.FieldWidths.TryGetValue(def.Key, out var w) ? w : def.DefaultWidth);
            col.HeaderStyle = headerStyle;
            Grid.Columns.Add(col);
        }

        if (cfg.ShowSparkline)
        {
            var sparkCol = new DataGridTemplateColumn
            {
                Header = "走势",
                Width = 124,
                IsReadOnly = true,
                CellTemplate = BuildSparklineTemplate(),
            };
            Grid.Columns.Add(sparkCol);
        }

        // 名称列左对齐（旧版口径），其余右对齐
        if (Grid.Columns[0] is DataGridTemplateColumn first
            && first.CellTemplate?.VisualTree is FrameworkElementFactory ff)
            ff.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);

        AutoSizeWindow();
    }

    /// <summary>
    /// 按内容自适应窗口尺寸（旧版语义）：宽 = 列宽合计 + 边距；高 = 行数 × 行高 + 表头与量能栏。
    /// 超出工作区时钳制（此时表格内部滚动兜底），避免列被硬裁剪只见前几列。
    /// </summary>
    private void AutoSizeWindow()
    {
        double width = 44; // 卡片内边距 + 表格边距
        foreach (var col in Grid.Columns)
            width += col.Width.IsAbsolute ? col.Width.Value : 80;
        var maxW = SystemParameters.WorkArea.Width * 0.95;
        Width = Math.Clamp(width, MinWidth, maxW);

        var rows = _vm.Rows.Count;
        var groupCount = _vm.Settings.GroupByCategory && rows > 0
            ? _vm.Rows.Select(r => r.CategoryName).Distinct().Count()
            : 0;
        var height = rows * Grid.RowHeight
                     + groupCount * 26   // 分组头
                     + 24                // 列表头
                     + 24                // 量能栏
                     + 18;               // 卡片边距
        var maxH = SystemParameters.WorkArea.Height - 20;
        Height = Math.Clamp(height, 240, maxH);
    }

    private DataGridTemplateColumn BuildValueColumn(FieldDefinitions.FieldDef def)
    {
        var col = new DataGridTemplateColumn
        {
            Header = def.Display,
            IsReadOnly = true,
            SortMemberPath = def.Key, // 表头点击排序时按此识别字段
        };

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding($"[{def.Key}].Text"));
        textFactory.SetBinding(TextBlock.ForegroundProperty,
            new System.Windows.Data.Binding($"[{def.Key}].Tone") { Converter = ToneToBrushConverter.Instance });
        textFactory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        textFactory.SetValue(MarginProperty, new Thickness(0, 0, 8, 0)); // 列间视觉间距

        var template = new DataTemplate { DataType = typeof(StockRowViewModel) };
        template.VisualTree = textFactory;
        col.CellTemplate = template;
        return col;
    }

    private DataTemplate BuildSparklineTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(Sparkline));
        factory.SetValue(FrameworkElement.HeightProperty, 18d);
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 2, 0, 2)); // 与振幅列保持间距
        factory.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        factory.SetBinding(Sparkline.ValuesProperty,
            new System.Windows.Data.Binding(nameof(StockRowViewModel.SparkValues)));

        var template = new DataTemplate { DataType = typeof(StockRowViewModel) };
        template.VisualTree = factory;
        return template;
    }

    /// <summary>共享表头样式：继承主题隐式样式（橙色前景等），叠加单击排序与双击显隐。</summary>
    private Style CreateHeaderStyle()
    {
        // 必须基于主题隐式样式：否则 Foreground（AccentBrush 橙色）等丢失，
        // 表头文字回退默认黑色，深色主题下不可见
        var style = new Style(typeof(DataGridColumnHeader), (Style)FindResource(typeof(DataGridColumnHeader)));
        style.Setters.Add(new EventSetter(DataGridColumnHeader.ClickEvent,
            new RoutedEventHandler(Header_Click)));
        style.Setters.Add(new EventSetter(DataGridColumnHeader.MouseDoubleClickEvent,
            new MouseButtonEventHandler(Header_DoubleClick)));
        return style;
    }

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (sender is DataGridColumnHeader { Column.SortMemberPath: { } key })
            _vm.SetSort(key);
    }

    private void Header_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ToggleVisibility();
    }

    // ---------------------------
    // 底部量能栏（富文本）
    // ---------------------------

    private void RebuildAmountText()
    {
        var cfg = _vm.Settings;
        var inlines = AmountText.Inlines;
        inlines.Clear();
        AmountText.FontSize = Math.Max(10, cfg.FontSize + 1);

        if (string.IsNullOrEmpty(_vm.TotalAmountText))
        {
            inlines.Add(new Run(_vm.StatusText) { Foreground = SubBrush(), FontSize = cfg.FontSize - 1 });
            return;
        }

        inlines.Add(new Run("今日沪深两市成交额：")
        {
            Foreground = AccentBrush(),
            FontWeight = FontWeights.Bold,
        });

        var amountRun = new Run($"{_vm.TotalAmountText} 亿")
        {
            Foreground = FlatStrongBrush(),
            FontWeight = FontWeights.Bold,
            FontSize = AmountText.FontSize + 1,
            Cursor = Cursors.Hand,
        };
        amountRun.MouseLeftButtonUp += (_, _) =>
            ShowTooltip(_vm.YesterdaySummary);
        Typography.SetNumeralAlignment(amountRun, FontNumeralAlignment.Tabular);
        inlines.Add(amountRun);

        if (!string.IsNullOrEmpty(_vm.AmountDiffText))
        {
            inlines.Add(new Run(_vm.AmountDiffText)
            {
                Foreground = _vm.AmountDiffTone switch
                {
                    "up" => UpBrush(),
                    "down" => DownBrush(),
                    _ => SubBrush(),
                },
                FontWeight = FontWeights.Bold,
            });
        }

        var status = new Run($"  [{_vm.StatusText}]")
        {
            Foreground = StatusBrush(),
            FontSize = cfg.FontSize - 1,
            Cursor = Cursors.Hand,
        };
        status.MouseLeftButtonUp += async (_, _) => await _vm.RefreshNowCommand.ExecuteAsync(null);
        inlines.Add(status);
    }

    private void AmountText_MouseMove(object sender, MouseEventArgs e)
    {
        // 预留：悬停状态段展示上次刷新时间（当前 ToolTip 由状态文本自身承载）
    }

    private void AmountText_MouseLeave(object sender, MouseEventArgs e) => _tip.IsOpen = false;

    private void ShowTooltip(string text)
    {
        _tip.Content = text;
        _tip.PlacementTarget = this;
        _tip.IsOpen = true;
    }

    // 主题画刷快捷访问
    private Brush AccentBrush() => (Brush)Application.Current.Resources["AccentBrush"];
    private Brush UpBrush() => (Brush)Application.Current.Resources["UpBrush"];
    private Brush DownBrush() => (Brush)Application.Current.Resources["DownBrush"];
    private Brush SubBrush() => (Brush)Application.Current.Resources["SubFgBrush"];
    private Brush StatusBrush() => (Brush)Application.Current.Resources["StatusBrush"];
    private Brush FlatStrongBrush() => (Brush)Application.Current.Resources["FlatBrush"];

    // ---------------------------
    // 行交互
    // ---------------------------

    /// <summary>
    /// 悬停显示股票信息：赋给行原生 ToolTip（自动延迟与定位）。
    /// 不用自绘 Popup 式 ToolTip —— 弹在鼠标点会盖住行，触发 Leave/Enter 循环闪烁并干扰右键。
    /// </summary>
    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is DataGridRow gridRow && gridRow.DataContext is StockRowViewModel row)
            gridRow.ToolTip = $"📈 {row.Current.Name}（{row.CodeDisplay}）";
    }

    private void Row_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow { DataContext: StockRowViewModel row })
        {
            e.Handled = true;
            _vm.RequestMinute(row);
        }
    }

    // ---------------------------
    // 右键菜单（与旧版条目对齐）
    // ---------------------------

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        var info1 = new MenuItem
        {
            Header = $"⏱️ 刷新间隔：{_vm.Settings.RefreshIntervalMs / 1000.0:0.#} 秒",
            IsEnabled = false,
        };
        var info2 = new MenuItem
        {
            Header = $"⌨️ 快捷键：{HotkeyParser.NormalizeDisplay(_vm.Settings.Hotkey)}",
            IsEnabled = false,
        };
        var hotkeyItem = new MenuItem { Header = "🧩 修改快捷键" };
        hotkeyItem.Click += (_, _) =>
        {
            var dlg = new HotkeyDialog(_vm.Settings.Hotkey) { Owner = this };
            if (dlg.ShowDialog() == true && dlg.Result is { } combo)
            {
                var cfg = _vm.Settings.Clone();
                cfg.Hotkey = combo;
                _vm.ApplySettings(cfg);
                BuildContextMenu();
            }
        };

        menu.Items.Add(info1);
        menu.Items.Add(info2);
        menu.Items.Add(hotkeyItem);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });

        var add = new MenuItem { Header = "➕ 添加股票" };
        add.Click += async (_, _) =>
        {
            var api = App.Services.GetRequiredService<ITencentQuoteApi>();
            var dlg = new InputDialog(
                "添加股票",
                "股票代码 / 名称 / 拼音关键字：",
                "例如：600390、浦发、pfyh、00700、AAPL",
                kw => api.SearchSuggestAsync(kw))
            {
                Owner = this,
            };
            if (dlg.ShowDialog() != true) return;

            var result = await _vm.AddStockAsync(dlg.SelectedMatch?.Code ?? dlg.Input);
            switch (result)
            {
                case AddStockResult.Ok:
                    break;
                case AddStockResult.AlreadyExists:
                    MessageBox.Show(this, "该股票已在自选列表中。", "添加股票",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case AddStockResult.FetchFailed:
                    MessageBox.Show(this, "无法获取该股票行情，请检查代码是否正确。", "添加股票",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                default:
                    MessageBox.Show(this, "代码无效，请输入有效的股票代码。", "添加股票",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        };

        var remove = new MenuItem { Header = "🗑️ 删除股票" };
        remove.Click += (_, _) =>
        {
            if (_vm.SelectedRow is null)
            {
                MessageBox.Show(this, "请先选中要删除的股票。", "删除股票",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _vm.RemoveSelected();
        };

        menu.Items.Add(add);
        menu.Items.Add(remove);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });

        var orderMenu = new MenuItem { Header = "📊 调整顺序" };
        AddOrderItem(orderMenu, "🔝 移至顶部", 0);
        AddOrderItem(orderMenu, "⬆️ 上移", -1);
        AddOrderItem(orderMenu, "⬇️ 下移", 1);
        AddOrderItem(orderMenu, "🔚 移至底部", 2);
        menu.Items.Add(orderMenu);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });

        var windowMenu = new MenuItem { Header = "🪟 窗口设置" };
        var topItem = new MenuItem { Header = "📌 取消置顶" };
        topItem.Click += (_, _) =>
        {
            Topmost = !Topmost;
            topItem.Header = Topmost ? "📌 取消置顶" : "📌 置顶窗口";
        };
        var hideItem = new MenuItem { Header = "🙈 隐藏窗口" };
        hideItem.Click += (_, _) => ToggleVisibility();
        var lockItem = new MenuItem
        {
            Header = _vm.Settings.Locked ? "🔓 解除锁定" : "🔒 锁定位置",
        };
        lockItem.Click += (_, _) =>
        {
            _vm.ToggleLock();
            lockItem.Header = _vm.Settings.Locked ? "🔓 解除锁定" : "🔒 锁定位置";
        };
        var resetPos = new MenuItem { Header = "🎯 初始化窗口位置" };
        resetPos.Click += (_, _) =>
        {
            Left = 100;
            Top = 100;
            _vm.SaveWindowPosition(Left, Top);
        };
        windowMenu.Items.Add(topItem);
        windowMenu.Items.Add(hideItem);
        windowMenu.Items.Add(lockItem);
        windowMenu.Items.Add(resetPos);
        menu.Items.Add(windowMenu);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });

        var themeItem = new MenuItem { Header = "🌓 切换主题" };
        themeItem.Click += (_, _) => _vm.ToggleTheme();
        menu.Items.Add(themeItem);

        var settingsItem = new MenuItem { Header = "⚙️ 设置中心" };
        settingsItem.Click += (_, _) => _vm.RequestSettings();
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator { Style = (Style)FindResource("MenuSeparatorStyle") });

        var aboutItem = new MenuItem { Header = "ℹ️ 关于程序" };
        aboutItem.Click += (_, _) => _vm.RequestAbout();
        menu.Items.Add(aboutItem);

        var quitItem = new MenuItem { Header = "❌ 退出程序" };
        quitItem.Click += (_, _) => _vm.RequestQuit();
        menu.Items.Add(quitItem);

        Grid.ContextMenu = menu;
        this.ContextMenu = menu;
    }

    private void AddOrderItem(MenuItem parent, string header, int direction)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            if (_vm.SelectedRow is null)
            {
                MessageBox.Show(this, "请先选中要调整的股票。", "调整顺序",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _vm.MoveSelected(direction);
        };
        parent.Items.Add(item);
    }

    private void OpenSettings()
    {
        var dlg = new SettingsWindow(_vm) { Owner = this };
        dlg.ShowDialog();
        if (dlg.Saved) BuildContextMenu(); // 刷新间隔 / 快捷键信息行
    }

    private void ShowAbout()
    {
        new AboutWindow
        {
            HotkeyDisplay = HotkeyParser.NormalizeDisplay(_vm.Settings.Hotkey),
            Owner = this,
        }.ShowDialog();
    }

    // ---------------------------
    // 拖拽 / 显隐
    // ---------------------------

    protected override bool CanDragWindow(object source)
    {
        if (_vm.Settings.Locked) return false;
        return base.CanDragWindow(source);
    }

    /// <summary>拖动结束后持久化窗口位置（替换旧 DragStrip 的保存）。</summary>
    protected override void OnWindowDragged() => _vm.SaveWindowPosition(Left, Top);

    /// <summary>窗口显隐切换（热键 / 托盘 / 双击表头），带 120ms 淡入淡出（不影响用户设置的不透明度）。</summary>
    public void ToggleVisibility()
    {
        if (_animatingVisibility) return; // 防重入：动画施放期间忽略

        var target = Math.Clamp(_vm.Settings.OpacityPercent, 10, 100) / 100.0;

        if (IsVisible)
        {
            _animatingVisibility = true;
            // 淡出后再隐藏
            var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(FadeDurationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fadeOut.Completed += (_, _) =>
            {
                Hide();
                BeginAnimation(OpacityProperty, null); // 移除瞬态动画，复位依赖属性
                Opacity = target; // 复位为目标不透明度，供下次显示用
                _animatingVisibility = false;
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        else
        {
            _animatingVisibility = true;
            BeginAnimation(OpacityProperty, null); // 确保起始不透明度为 0
            Opacity = 0;
            Show();
            Activate();
            // 淡入到用户设置的不透明度
            var fadeIn = new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(FadeDurationMs))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            };
            fadeIn.Completed += (_, _) =>
            {
                BeginAnimation(OpacityProperty, null); // 移除瞬态动画，稳定为 target
                Opacity = target; // 最终不透明度与设置一致
                _animatingVisibility = false;
            };
            BeginAnimation(OpacityProperty, fadeIn);
        }
    }

    /// <summary>应用外观（启动与设置保存后调用）。</summary>
    private bool _positionApplied;

    public void ApplyAppearance()
    {
        var cfg = _vm.Settings;
        Opacity = 1.0; // 整窗不透明；背景 alpha 由 ThemeManager.ApplyOpacity 控制
        RowFontFamily = new FontFamily(cfg.FontFamily);
        RowFontSize = cfg.FontSize + 2;
        RowFontStyle = cfg.FontItalic ? FontStyles.Italic : FontStyles.Normal;

        if (!_positionApplied)
        {
            _positionApplied = true;
            Left = cfg.WindowX;
            Top = cfg.WindowY;
            EnsureOnScreen();
        }
    }

    /// <summary>窗口拖出屏幕（换显示器 / 分辨率变化）后自动拉回可见区域（增强：越界纠正）。</summary>
    public void EnsureOnScreen()
    {
        const int margin = 40;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        if (double.IsNaN(w)) w = 420;
        if (double.IsNaN(h)) h = 360;

        var offScreen = Left + w < virtualLeft + margin || Left > virtualRight - margin
                        || Top + h < virtualTop + margin || Top > virtualBottom - margin;
        if (!offScreen) return;

        Left = Math.Max(virtualLeft + margin, (virtualRight - virtualLeft) / 2 - w / 2);
        Top = Math.Max(virtualTop + margin, (virtualBottom - virtualTop) / 2 - h / 2);
        _vm.SaveWindowPosition(Left, Top);
    }

    /// <summary>键盘：Delete 删除选中股票（固定股由仓储层拒绝）。</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Delete && Grid.IsKeyboardFocusWithin && _vm.SelectedRow is not null)
        {
            _vm.RemoveSelected();
            e.Handled = true;
        }
    }

    // ---------------------------
    // 全局热键（RegisterHotKey）
    // ---------------------------

    private void HookHotkey()
    {
        _hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
        RegisterHotkey(_vm.Settings.Hotkey);
    }

    private void RegisterHotkey(string combo)
    {
        if (_hwndSource is null) return;
        var hwnd = _hwndSource.Handle;
        HotkeyNative.UnregisterHotKey(hwnd, HotkeyId);

        if (!HotkeyParser.TryParse(combo, out var mod, out var vk)) return;
        if (!HotkeyNative.RegisterHotKey(hwnd, HotkeyId, mod, vk))
            ShowTooltip($"⚠️ 全局热键 {HotkeyParser.NormalizeDisplay(combo)} 注册失败，可能已被占用");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNative.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            ToggleVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---------------------------
    // 退出
    // ---------------------------

    public string SettingsHotkey => _vm.Settings.Hotkey;

    /// <summary>托盘"退出"入口。</summary>
    public void QuitFromTray() => QuitApp();

    private void QuitApp()
    {
        _vm.Shutdown();
        UnregisterHotkeySafe();
        Application.Current.Shutdown();
    }

    private void UnregisterHotkeySafe()
    {
        try
        {
            if (_hwndSource is not null)
                HotkeyNative.UnregisterHotKey(_hwndSource.Handle, HotkeyId);
        }
        catch
        {
            // 退出清理失败可忽略
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        UnregisterHotkeySafe();
        base.OnClosed(e);
    }
}

/// <summary>色调 → 画刷转换器（查当前主题资源）。</summary>
public sealed class ToneToBrushConverter : System.Windows.Data.IValueConverter
{
    public static ToneToBrushConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var key = value switch
        {
            "up" => "UpBrush",
            "down" => "DownBrush",
            "flat" => "FlatBrush",
            "fail" => "FailBrush",
            _ => "FgBrush",
        };
        return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
