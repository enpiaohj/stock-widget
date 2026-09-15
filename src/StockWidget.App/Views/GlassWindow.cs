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

    /// <summary>整窗背景拖拽：在非交互控件的空白区域按住左键移动窗口。</summary>
    private void OnPreviewDragDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ButtonState != MouseButtonState.Pressed) return;
        if (!CanDragWindow(e.OriginalSource)) return;
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

    /// <summary>
    /// 命中交互控件（按钮/输入/滑杆/表格单元格/表头/滚动条/页签等）时不拖动。
    /// 子类可覆写扩展规则（返回 false 阻止拖动）。
    /// </summary>
    protected virtual bool CanDragWindow(object source)
    {
        for (var d = source as DependencyObject; d is not null; d = GetVisualParent(d))
        {
            if (d is Button or TextBox or ComboBox or ComboBoxItem or Slider or CheckBox
                or RadioButton or Thumb or ScrollBar or TabItem or ListBox or ListBoxItem
                or DataGridCell or DataGridColumnHeader or DataGridRow or ScrollViewer)
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
