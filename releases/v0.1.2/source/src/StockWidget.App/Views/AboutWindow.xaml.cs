using System.Reflection;
using System.Windows;

namespace StockWidget.App.Views;

public partial class AboutWindow : GlassWindow
{
    public required string HotkeyDisplay { get; init; }

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        TitleText.Text = $"股票小插件 v{version?.Major ?? 1}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
        DeveloperText.Text = "开发者：一叶花知秋";
        HotkeyText.Text = $"快捷键：{HotkeyDisplay}";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
