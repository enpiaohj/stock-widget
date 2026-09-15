using System.IO;
using System.Windows;
using System.Windows.Media;
using StockWidget.App.Views;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using StockWidget.Core.Models;

namespace StockWidget.App.Services;

// ---------------------------
// 主题管理：dark / light / system，热切换资源字典并刷新 DWM
// ---------------------------
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    public event Action<string>? EffectiveThemeChanged; // "dark" / "light"

    private SolidColorBrush? _originalBg;
    private LinearGradientBrush? _originalGradient;

    public string CurrentEffectiveTheme { get; private set; } = "dark";

    /// <summary>应用主题（settings.Theme：dark / light / system）。</summary>
    public void Apply(string themeSetting)
    {
        var effective = themeSetting switch
        {
            "dark" => "dark",
            "light" => "light",
            _ => IsSystemDark() ? "dark" : "light",
        };
        if (effective == CurrentEffectiveTheme && Application.Current.Resources.MergedDictionaries.Count > 0)
            return;

        ApplyDictionary(effective);
    }

    private void ApplyDictionary(string effective)
    {
        var app = Application.Current;

        // 每次重新加载字典（曾尝试缓存实例复用，WPF 下有状态残留导致切换异常，回退）
        var dict = new ResourceDictionary { Source = new Uri($"Themes/{(effective == "dark" ? "Dark" : "Light")}.xaml", UriKind.Relative) };

        // 替换主题字典（Controls.xaml 保留在原位）
        var old = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Dark.xaml") == true
                                 || d.Source?.OriginalString.Contains("Light.xaml") == true);
        if (old is not null)
            app.Resources.MergedDictionaries.Remove(old);
        app.Resources.MergedDictionaries.Add(dict);

        // 缓存主题原始刷（透明度调整需从原始色克隆，避免多次应用累积变全透）
        _originalBg = dict["BgBrush"] as SolidColorBrush;
        _originalGradient = dict["CardGradientBrush"] as LinearGradientBrush;

        CurrentEffectiveTheme = effective;

        foreach (Window w in app.Windows)
        {
            if (w is GlassWindow gw)
                gw.ApplyBackdrop(effective == "dark");
        }

        EffectiveThemeChanged?.Invoke(effective);
    }

    /// <summary>
    /// 按强调色 key 派生并覆盖 Application.Resources 中强调相关刷（本地资源优先于主题字典同名 key）。
    /// 默认橙时清空覆盖，回落到主题字典自身的强调色。
    /// </summary>
    public void ApplyAccent(string accentKey)
    {
        var app = Application.Current;
        var pal = AccentPalette.FromKey(accentKey);

        if (accentKey.Equals("orange", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(accentKey))
        {
            // 默认橙：移除本地覆盖，回落到主题字典内置强调色
            app.Resources.Remove("AccentBrush");
            app.Resources.Remove("AccentSoftBrush");
            app.Resources.Remove("HeaderLineBrush");
            app.Resources.Remove("ControlBorderBrush");
            app.Resources.Remove("GroupHeaderBrush");
            app.Resources.Remove("MenuHoverBrush");
            return;
        }

        // 非默认 → 用派生刷覆盖（注意 AccentBrush 需保持不透明主色，其余带合适 alpha）
        app.Resources["AccentBrush"] = ToBrush(pal.Main);
        app.Resources["AccentSoftBrush"] = ToBrush(pal.Soft);
        app.Resources["HeaderLineBrush"] = ToBrush(pal.Strong);
        app.Resources["GroupHeaderBrush"] = ToBrush(pal.Strong);
        app.Resources["MenuHoverBrush"] = ToBrush(pal.Soft);
        app.Resources["ControlBorderBrush"] = ToBrush(pal.Soft);
    }

    /// <summary>ARGB-as-long（0xAARRGGBB）→ 画笔；移位后掩去高 8 位以防符号扩展。</summary>
    private static SolidColorBrush ToBrush(long argb)
    {
        var a = (byte)((argb >> 24) & 0xFF);
        var r = (byte)((argb >> 16) & 0xFF);
        var g = (byte)((argb >> 8) & 0xFF);
        var b = (byte)(argb & 0xFF);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    /// <summary>
    /// 按不透明度百分比（10–100）调整窗口底色 alpha：仅背景透桌面，文字/数据保持不透明清晰可读。
    /// 100 = 完全不透明；数值越小越透。从主题原始刷克隆，多次应用不累积。
    /// </summary>
    public void ApplyOpacity(int percent)
    {
        var app = Application.Current;
        if (_originalBg is null) return; // 主题字典尚未加载

        // 所见即所得：100=完全不透，10=透 90%（文字本身不透明）
        var alpha = Math.Clamp(percent, 10, 100) / 100.0;

        if (_originalBg is { } bg)
        {
            var c = bg.Color;
            // 直接以档位换算最终不透明度（原色自带 97% alpha，若相乘则 10 档仍微微透出后面）
            app.Resources["BgBrush"] = new SolidColorBrush(
                Color.FromArgb((byte)Math.Round(255 * alpha), c.R, c.G, c.B));
        }

        if (_originalGradient is { } grad)
        {
            var clone = grad.Clone();
            foreach (var stop in clone.GradientStops)
            {
                var a = (byte)Math.Clamp(Math.Round(stop.Color.A * alpha), 0, 255);
                stop.Color = Color.FromArgb(a, stop.Color.R, stop.Color.G, stop.Color.B);
            }
            clone.Freeze();
            app.Resources["CardGradientBrush"] = clone;
        }
    }

    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i ? i == 0 : true;
        }
        catch
        {
            return true;
        }
    }
}

// ---------------------------
// 开机自启（HKCU Run 键）
// ---------------------------
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StockWidget";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, false);
        }
    }
}

// ---------------------------
// 托盘图标工厂：app.ico + 大盘涨跌色点叠加
// ---------------------------
public static class TrayIconFactory
{
    private static readonly BitmapSource? BaseIcon = LoadBaseIcon();

    public static ImageSource Create(Color overlayColor, bool showOverlay = true)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            if (BaseIcon is not null)
                dc.DrawImage(BaseIcon, new Rect(0, 0, 32, 32));
            else
                dc.DrawRectangle(Brushes.DarkOrange, null, new Rect(0, 0, 32, 32));

            if (showOverlay)
            {
                dc.DrawEllipse(new SolidColorBrush(overlayColor),
                    new Pen(Brushes.White, 1.2),
                    new Point(25, 25), 5.2, 5.2);
            }
        }

        var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource? LoadBaseIcon()
    {
        try
        {
            // 开发态与单文件发布均可用相对 pack URI
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.Width).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
