using System.IO;
using System.Threading;
using System.Windows;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using StockWidget.App.Services;
using StockWidget.App.ViewModels;
using StockWidget.App.Views;
using StockWidget.Core;
using StockWidget.Core.Services;

namespace StockWidget.App;

/// <summary>
/// 应用入口：单实例互斥、DI 装配、托盘初始化、旧版数据导入、主窗口启动。
/// </summary>
public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    public static IServiceProvider Services { get; private set; } = null!;
    private TaskbarIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        WriteCrashLog("step", new Exception("OnStartup 进入"));
        try
        {
            StartupCore(e);
        }
        catch (Exception ex)
        {
            WriteCrashLog("OnStartup 异常", ex);
            throw;
        }
    }

    private void StartupCore(StartupEventArgs e)
    {
        // 单实例互斥：二次启动时激活已有窗口后退出
        _singleInstanceMutex = new Mutex(true, @"Local\StockWidget.SingleInstance", out var isNew);
        WriteCrashLog("step", new Exception($"互斥锁 isNew={isNew}"));
        if (!isNew)
        {
            MessageBox.Show("股票小插件已在运行。", "股票小插件",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 全局异常兜底：记录日志，保证 UI 不崩（与旧版"吞异常保 UI"策略一致）
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("UI", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("Unhandled", args.ExceptionObject as Exception);

        base.OnStartup(e);

        // DI 装配（Core：数据库迁移在注册时执行）
        var services = new ServiceCollection();
        services.AddStockWidgetCore();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        Services = services.BuildServiceProvider();
        WriteCrashLog("step", new Exception("DI 装配完成"));

        // 旧版数据导入（首启动扫描 exe 目录）
        TryImportLegacy();

        // 托盘
        _tray = new TaskbarIcon
        {
            ToolTipText = "股票小插件",
            IconSource = TrayIconFactory.Create(default, showOverlay: false),
        };
        ToastService.Init(_tray);
        BuildTrayMenu();

        // 主窗口
        var window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private void TryImportLegacy()
    {
        try
        {
            var importer = Services.GetRequiredService<ILegacyImporter>();
            if (importer.IsLegacyImported()) return;

            // 候选目录：exe 目录、%LOCALAPPDATA%\stockTool（设置中心还可手动选择目录导入）
            var candidates = new[]
            {
                AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "stockTool"),
            };
            foreach (var dir in candidates.Where(Directory.Exists))
            {
                var result = importer.ImportFromDirectory(dir);
                if (result.HasAnything)
                    System.Diagnostics.Debug.WriteLine(
                        $"[旧版导入] 配置:{result.ConfigImported} 历史:{result.HistoryDays}天 {result.Error}");
                if (importer.IsLegacyImported()) break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[旧版导入失败] {ex.Message}");
        }
    }

    private void BuildTrayMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();
        var style = Resources["MenuSeparatorStyle"] as System.Windows.Style;

        var toggle = new System.Windows.Controls.MenuItem { Header = "显示窗口" };
        toggle.Click += (_, _) => (MainWindow as Views.MainWindow)?.ToggleVisibility();
        menu.Items.Add(toggle);
        menu.Items.Add(new System.Windows.Controls.Separator { Style = style });

        var top = new System.Windows.Controls.MenuItem { Header = "窗口置顶" };
        top.Click += (_, _) => { if (MainWindow is MainWindow w) w.Topmost = true; };
        var bottom = new System.Windows.Controls.MenuItem { Header = "取消置顶" };
        bottom.Click += (_, _) => { if (MainWindow is MainWindow w) w.Topmost = false; };
        menu.Items.Add(top);
        menu.Items.Add(bottom);
        menu.Items.Add(new System.Windows.Controls.Separator { Style = style });

        var resetPos = new System.Windows.Controls.MenuItem { Header = "初始化窗口位置" };
        resetPos.Click += (_, _) =>
        {
            if (MainWindow is MainWindow w)
            {
                w.Left = 100;
                w.Top = 100;
            }
        };
        menu.Items.Add(resetPos);
        menu.Items.Add(new System.Windows.Controls.Separator { Style = style });

        var about = new System.Windows.Controls.MenuItem { Header = "关于" };
        about.Click += (_, _) =>
        {
            if (MainWindow is MainWindow w)
                new AboutWindow { HotkeyDisplay = HotkeyParser.NormalizeDisplay(HotkeyOf(w)), Owner = w }.ShowDialog();
        };
        menu.Items.Add(about);

        var quit = new System.Windows.Controls.MenuItem { Header = "退出" };
        quit.Click += (_, _) =>
        {
            _tray?.Dispose();
            (MainWindow as Views.MainWindow)?.QuitFromTray();
        };
        menu.Items.Add(quit);

        _tray!.ContextMenu = menu;
        _tray.TrayMouseDoubleClick += (_, _) => (MainWindow as Views.MainWindow)?.ToggleVisibility();
    }

    private static string HotkeyOf(Views.MainWindow w) => w.SettingsHotkey;

    private static string? _logFile;

    /// <summary>启动/崩溃日志（诊断用）：%LOCALAPPDATA%\StockWidget\startup.log</summary>
    internal static void WriteCrashLog(string tag, Exception? ex)
    {
        try
        {
            if (_logFile is null)
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StockWidget");
                Directory.CreateDirectory(dir);
                _logFile = Path.Combine(dir, "startup.log");
            }
            File.AppendAllText(_logFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{tag}] {ex?.GetType().Name}: {ex?.Message}\n{ex?.StackTrace}\n---\n");
            System.Diagnostics.Debug.WriteLine($"[{tag}] {ex}");
        }
        catch
        {
            // 日志失败忽略
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
