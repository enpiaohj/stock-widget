using System.IO;
using System.Windows;
using System.Windows.Media;
using StockWidget.App.Views;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace StockWidget.App.Services;

// ---------------------------
// 主题管理：dark / light / system，热切换资源字典并刷新 DWM
// ---------------------------
public sealed class ThemeManager
{
    public static ThemeManager Instance { get; } = new();

    public event Action<string>? EffectiveThemeChanged; // "dark" / "light"

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
        var uri = new Uri($"Themes/{(effective == "dark" ? "Dark" : "Light")}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        // 替换主题字典（Controls.xaml 保留在原位）
        var old = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Dark.xaml") == true
                                 || d.Source?.OriginalString.Contains("Light.xaml") == true);
        if (old is not null)
            app.Resources.MergedDictionaries.Remove(old);
        app.Resources.MergedDictionaries.Add(dict);

        CurrentEffectiveTheme = effective;

        foreach (Window w in app.Windows)
        {
            if (w is GlassWindow gw)
                gw.ApplyBackdrop(effective == "dark");
        }

        EffectiveThemeChanged?.Invoke(effective);
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
