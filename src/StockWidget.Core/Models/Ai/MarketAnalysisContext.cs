using StockWidget.Core.Data.Entities;

namespace StockWidget.Core.Models.Ai;

/// <summary>
/// 单只标的的行情分析上下文：本地"事实"数据（行情快照 + 日K + 技术指标），
/// 由调用方组装后交给 IAiAnalysisService。AI 仅负责解释，不负责生成事实。
/// </summary>
public sealed class MarketAnalysisContext
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>资产类型：指数 / ETF / 个股。</summary>
    public string AssetType { get; set; } = "";

    public DateTime AnalyzedAt { get; set; }

    /// <summary>当前行情快照。</summary>
    public QuoteData Quote { get; set; } = new();

    /// <summary>最近 120 根日K（升序，最近一根在末尾）。</summary>
    public IReadOnlyList<DailyKlineEntity> Klines { get; set; } = [];

    public decimal Ma5 { get; set; }
    public decimal Ma10 { get; set; }
    public decimal Ma20 { get; set; }

    public decimal Change5Pct { get; set; }
    public decimal Change20Pct { get; set; }
    public decimal Change60Pct { get; set; }

    /// <summary>区间（与 Klines 同范围）最高 / 最低价。</summary>
    public decimal RangeHigh { get; set; }
    public decimal RangeLow { get; set; }
}
