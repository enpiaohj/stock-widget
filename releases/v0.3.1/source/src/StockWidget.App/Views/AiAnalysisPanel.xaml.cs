using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using StockWidget.App.ViewModels;
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;

namespace StockWidget.App.Views;

/// <summary>
/// AI 行情分析侧面板：结构化展示 DeepSeek 分析结果，
/// 支持 Loading / Success / Failure / NotConfigured 状态切换。
/// </summary>
public partial class AiAnalysisPanel : UserControl
{
    private readonly AiAnalysisViewModel _vm;

    public event Action? RequestOpenSettings
    {
        add => _vm.RequestOpenSettings += value;
        remove => _vm.RequestOpenSettings -= value;
    }

    public AiAnalysisPanel(IAiAnalysisService aiService, ISettingsService settingsService)
    {
        InitializeComponent();
        _vm = new AiAnalysisViewModel(aiService, settingsService);
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AiAnalysisViewModel.State) or nameof(AiAnalysisViewModel.Result))
                ApplyState();
        };
        DataContext = _vm;
        ApplyState();
    }

    private void ApplyState()
    {
        LoadingSection.Visibility = _vm.State == AiPanelState.Loading ? Visibility.Visible : Visibility.Collapsed;
        FailureSection.Visibility = _vm.State == AiPanelState.Failure ? Visibility.Visible : Visibility.Collapsed;
        NotConfiguredSection.Visibility = _vm.State == AiPanelState.NotConfigured ? Visibility.Visible : Visibility.Collapsed;
        SuccessSection.Visibility = _vm.State == AiPanelState.Success ? Visibility.Visible : Visibility.Collapsed;

        StockNameText.Text = _vm.StockName;
        AnalyzedAtText.Text = _vm.AnalyzedAtText;
        FailureTextBlock.Text = _vm.FailureText;

        if (_vm.Result is { } r)
        {
            OverallStateText.Text = r.OverallState;
            SummaryText.Text = r.Summary;
            TrendText.Text = r.TrendAnalysis;
            VolumeText.Text = r.VolumeAnalysis;
            ResistanceText.Text = ToLines(r.ResistanceLevels);
            SupportText.Text = ToLines(r.SupportLevels);
            RiskText.Text = ToLines(r.RiskObservations.Select(x => "• " + x));
            DisclaimerText.Text = string.IsNullOrWhiteSpace(r.Disclaimer)
                ? "AI 仅基于当前行情与历史数据分析，不构成投资建议。"
                : r.Disclaimer;
        }
    }

    private static string ToLines(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items);

    /// <summary>启动分析（forceRefresh=true 跳过缓存）。</summary>
    public void Analyze(Func<MarketAnalysisContext?> buildContext, bool forceRefresh = false) =>
        _ = _vm.AnalyzeAsync(buildContext, forceRefresh);

    /// <summary>窗口关闭时取消进行中的请求。</summary>
    public void Cancel() => _vm.Cancel();

    private void Retry_Click(object sender, RoutedEventArgs e) => _ = _vm.Retry();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) => _vm.OpenSettings();
}
