using CommunityToolkit.Mvvm.ComponentModel;
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;

namespace StockWidget.App.ViewModels;

/// <summary>AI Panel 内部状态。</summary>
public enum AiPanelState
{
    /// <summary>分析中（Loading）。</summary>
    Loading,

    /// <summary>成功，展示结构化结果。</summary>
    Success,

    /// <summary>失败（含友好错误与重试）。</summary>
    Failure,

    /// <summary>尚未配置（引导前往设置）。</summary>
    NotConfigured,
}

/// <summary>
/// AI 行情分析面板 ViewModel：负责状态机（Loading/Success/Failure/NotConfigured），
/// 数据组装由窗口通过 buildContext 提供（行情事实来自本地）。
/// </summary>
public partial class AiAnalysisViewModel : ObservableObject
{
    private readonly IAiAnalysisService _aiService;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _cts;
    private Func<MarketAnalysisContext?>? _lastContextBuilder;

    [ObservableProperty] private AiPanelState _state = AiPanelState.Loading;
    [ObservableProperty] private string _stockName = "";
    [ObservableProperty] private string _analyzedAtText = "";
    [ObservableProperty] private AiMarketAnalysisResult? _result;
    [ObservableProperty] private string _failureText = "";
    [ObservableProperty] private bool _aiEnabled;

    /// <summary>请求打开设置页（设置 → AI 分析）。</summary>
    public event Action? RequestOpenSettings;

    public AiAnalysisViewModel(IAiAnalysisService aiService, ISettingsService settingsService)
    {
        _aiService = aiService;
        _settingsService = settingsService;
    }

    /// <summary>刷新"启用 AI 分析"开关状态（设置保存后调用）。</summary>
    public void RefreshEnabled() => AiEnabled = _settingsService.Current.Ai.Enabled;

    /// <summary>取消进行中的分析（窗口关闭时调用）。</summary>
    public void Cancel() => _cts?.Cancel();

    /// <summary>重新分析：强制跳过缓存。</summary>
    public async Task Retry()
    {
        if (_lastContextBuilder is not null)
            await AnalyzeAsync(_lastContextBuilder, forceRefresh: true).ConfigureAwait(true);
    }

    /// <summary>启动分析。buildContext 在后台线程执行（含 SQLite 查询）。</summary>
    public async Task AnalyzeAsync(Func<MarketAnalysisContext?> buildContext, bool forceRefresh = false)
    {
        _lastContextBuilder = buildContext;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        RefreshEnabled();
        if (!AiEnabled || string.IsNullOrWhiteSpace(_settingsService.Current.Ai.ApiKeyEncrypted))
        {
            State = AiPanelState.NotConfigured;
            return;
        }

        State = AiPanelState.Loading;
        FailureText = "";
        Result = null;

        try
        {
            var context = await Task.Run(() => buildContext(), ct).ConfigureAwait(true);
            if (context is null)
            {
                FailureText = "行情数据不足（暂无日K历史），无法分析。";
                State = AiPanelState.Failure;
                return;
            }

            StockName = context.Name;
            AnalyzedAtText = context.AnalyzedAt.ToString("yyyy-MM-dd HH:mm");

            var result = await _aiService.AnalyzeAsync(context, forceRefresh, ct).ConfigureAwait(true);
            Result = result;
            State = AiPanelState.Success;
        }
        catch (OperationCanceledException)
        {
            // 窗口关闭/重新分析导致的取消：保持现状
        }
        catch (AiNotConfiguredException ex)
        {
            FailureText = ex.Message;
            State = AiPanelState.NotConfigured;
        }
        catch (AiApiException ex)
        {
            FailureText = ex.Message;
            State = AiPanelState.Failure;
        }
        catch (Exception ex)
        {
            FailureText = $"AI 分析失败：{ex.Message}";
            State = AiPanelState.Failure;
        }
    }

    /// <summary>打开设置（面板按钮 Click 调用）。</summary>
    public void OpenSettings() => RequestOpenSettings?.Invoke();
}
