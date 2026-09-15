using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shell;

namespace StockWidget.App.Views;

/// <summary>
/// 无边框玻璃窗口基类：
/// - WindowChrome 去标题栏，保留 DWM 阴影（GlassFrameThickness 扩展）
/// - Win11 22H2+：系统级亚克力背景 + 圆角 + 深色模式
/// - Win10：退化为 Aero Glass 模糊 + 无圆角
/// </summary>
public abstract class GlassWindow : Window
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // 亚克力

    /// <summary>系统背景模糊是否生效（Win11 22H2+）。</summary>
    public bool BackdropEnabled { get; private set; }

    protected GlassWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true;
        SourceInitialized += (_, _) => ApplyChrome();
        PreviewMouseLeftButtonDown += OnPreviewDragDown;
    }

    // 位移判别拖拽状态：按下点与可拖标记
    private Point? _dragStartPoint;
    private bool _dragStartAllowed;

    /// <summary>行间虚线分隔可见性（行情主窗口按 ShowDividers 配置驱动，其他窗口默认隐藏）。</summary>
    public static readonly DependencyProperty RowDividerVisibilityProperty = DependencyProperty.Register(
        nameof(RowDividerVisibility), typeof(Visibility), typeof(GlassWindow),
        new PropertyMetadata(Visibility.Collapsed));

    public Visibility RowDividerVisibility
    {
        get => (Visibility)GetValue(RowDividerVisibilityProperty);
        set => SetValue(RowDividerVisibilityProperty, value);
    }

    /// <summary>
    /// 整窗拖拽（对齐旧版语义）：按下任意非交互区域（含表格行）记录起点；
    /// 位移超过系统阈值才 DragMove —— 轻点仍触发行选择 / tooltip / 双击，拖动两不冲突。
    /// </summary>
    private void OnPreviewDragDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed)
        {
            _dragStartPoint = null;
            return;
        }

        _dragStartAllowed = CanDragWindow(e.OriginalSource);
        if (!_dragStartAllowed)
        {
            _dragStartPoint = null;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        PreviewMouseMove += OnPreviewDragMove;
        PreviewMouseLeftButtonUp += OnPreviewDragUp;
    }

    private void OnPreviewDragMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is not { } start
            || e.LeftButton != MouseButtonState.Pressed)
        {
            EndDragTracking();
            return;
        }

        var pos = e.GetPosition(this);
        var moved = Math.Abs(pos.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
                    || Math.Abs(pos.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!moved) return;

        EndDragTracking();
        try
        {
            DragMove();
            OnWindowDragged();
        }
        catch
        {
            // 窗口非活动等状态下 DragMove 抛异常，忽略
        }
    }

    private void OnPreviewDragUp(object sender, MouseButtonEventArgs e) => EndDragTracking();

    private void EndDragTracking()
    {
        _dragStartPoint = null;
        PreviewMouseMove -= OnPreviewDragMove;
        PreviewMouseLeftButtonUp -= OnPreviewDragUp;
    }

    /// <summary>
    /// 命中真正交互控件（按钮/输入/滑杆/表头/滚动条/页签/列表等）时不拖动。
    /// 表格行 / 单元格允许拖动（轻点无位移仍正常选择行）。
    /// 注意：不排除 ScrollViewer —— DataGrid 内部模板自带 ScrollViewer，
    /// 若排除则表格区域永远不可拖；滚动条交互由 ScrollBar 排除保证。
    /// 子类可覆写扩展规则（返回 false 阻止拖动）。
    /// </summary>
    protected virtual bool CanDragWindow(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = GetVisualParent(d))
        {
            if (d is Button or TextBox or ComboBox or ComboBoxItem or Slider or CheckBox
                or RadioButton or Thumb or ScrollBar or TabItem or ListBox or ListBoxItem
                or DataGridColumnHeader)
                return false;
        }
        return true;
    }

    /// <summary>拖动结束后子类可覆写以持久化窗口位置。</summary>
    protected virtual void OnWindowDragged() { }

    private static DependencyObject? GetVisualParent(DependencyObject d) =>
        d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

    private void ApplyChrome()
    {
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(-1), // DWM 玻璃延伸到客户区 → 阴影 + 系统模糊
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        ApplyBackdrop();
    }

    /// <summary>应用/刷新系统背景与深浅色模式（主题切换时由 ThemeManager 调用）。</summary>
    public void ApplyBackdrop(bool darkTheme = true)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        BackdropEnabled = SetBackdrop(hwnd, darkTheme);
        UpdateBackgroundBrush();
    }

    internal static bool SetBackdrop(IntPtr hwnd, bool darkTheme)
    {
        try
        {
            // 深色模式（20 新、19 旧，逐个尝试）
            if (!IsSuccess(DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkTheme, sizeof(bool))))
            {
                var old = 19;
                DwmSetWindowAttribute(hwnd, old, ref darkTheme, sizeof(bool));
            }

            var backdrop = DWMSBT_TRANSIENTWINDOW;
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
            return IsSuccess(hr);
        }
        catch (Exception)
        {
            return false; // Win10 等：无系统背景，用窗口自身半透明底色
        }
    }

    internal static void ClearBackdrop(IntPtr hwnd)
    {
        try
        {
            var backdrop = DWMSBT_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 背景底色：系统模糊可用时用半透明底色叠加毛玻璃效果；
    /// 不可用（Win10）时用更不透明的底色保证可读性。子类可覆写。
    /// </summary>
    protected virtual void UpdateBackgroundBrush()
    {
        var app = System.Windows.Application.Current;
        var key = BackdropEnabled ? "BgBrush" : "BgFallbackBrush";
        Background = app.Resources[key] as System.Windows.Media.Brush
                     ?? app.Resources["BgBrush"] as System.Windows.Media.Brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref bool value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static bool IsSuccess(int hr) => hr == 0;
}
