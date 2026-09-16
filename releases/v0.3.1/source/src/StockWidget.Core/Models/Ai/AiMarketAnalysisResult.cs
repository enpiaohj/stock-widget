namespace StockWidget.Core.Models.Ai;

/// <summary>AI 行情分析结果（与 DeepSeek 约定的严格 JSON 结构，camelCase）。</summary>
public sealed class AiMarketAnalysisResult
{
    /// <summary>综合判断（如"震荡偏弱"）。</summary>
    public string OverallState { get; set; } = "";

    /// <summary>摘要。</summary>
    public string Summary { get; set; } = "";

    /// <summary>趋势结构。</summary>
    public string TrendAnalysis { get; set; } = "";

    /// <summary>量价关系。</summary>
    public string VolumeAnalysis { get; set; } = "";

    /// <summary>压力区域（如"3960～4015"）。</summary>
    public List<string> ResistanceLevels { get; set; } = [];

    /// <summary>支撑区域。</summary>
    public List<string> SupportLevels { get; set; } = [];

    /// <summary>风险观察要点。</summary>
    public List<string> RiskObservations { get; set; } = [];

    /// <summary>免责声明。</summary>
    public string Disclaimer { get; set; } = "";
}
