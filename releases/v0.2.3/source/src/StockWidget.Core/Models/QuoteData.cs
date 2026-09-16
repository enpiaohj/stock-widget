namespace StockWidget.Core.Models;

/// <summary>股票分类（用于自动分组显示）。</summary>
public enum StockCategory
{
    /// <summary>A 股个股</summary>
    Stock,

    /// <summary>指数（上证 / 深证等）</summary>
    Index,

    /// <summary>ETF / 场内基金</summary>
    Etf,

    /// <summary>港股</summary>
    HongKong,

    /// <summary>美股</summary>
    UsStock,
}

/// <summary>
/// 单只股票的一次行情结果。数值字段用 <see cref="decimal"/>? 表示，
/// null 表示接口未返回或解析失败（展示为 "-"）。
/// </summary>
public sealed record QuoteData
{
    /// <summary>规范化代码，如 sh600390。</summary>
    public string Code { get; init; } = "";

    /// <summary>名称。</summary>
    public string Name { get; init; } = "-";

    public decimal? Price { get; init; }

    /// <summary>涨跌幅（%）。</summary>
    public decimal? ChangePct { get; init; }

    /// <summary>成交量（手）。</summary>
    public decimal? Volume { get; init; }

    /// <summary>成交额（万元，腾讯原始口径）。</summary>
    public decimal? Amount { get; init; }

    /// <summary>换手率（%）。</summary>
    public decimal? Turnover { get; init; }

    public decimal? High { get; init; }
    public decimal? Low { get; init; }
    public decimal? Open { get; init; }
    public decimal? PrevClose { get; init; }

    /// <summary>总市值（亿）。</summary>
    public decimal? MarketCap { get; init; }

    /// <summary>振幅（%）。</summary>
    public decimal? Amplitude { get; init; }

    /// <summary>本次抓取是否成功（失败时 Price 为 null 且 Success=false）。</summary>
    public bool Success { get; init; }

    /// <summary>抓取时间（本地时间）。</summary>
    public DateTime FetchedAt { get; init; } = DateTime.Now;

    /// <summary>失败占位结果（保留旧值的场景由调用方处理）。</summary>
    public static QuoteData Failed(string code) => new()
    {
        Code = code,
        Success = false,
        Name = "-",
        FetchedAt = DateTime.Now,
    };

    /// <summary>按字段名取值（供通用表格列渲染），未知名返回 null。</summary>
    public decimal? GetField(string key) => key switch
    {
        "price" => Price,
        "change" => ChangePct,
        "volume" => Volume,
        "amount" => Amount,
        "turnover" => Turnover,
        "high" => High,
        "low" => Low,
        "open" => Open,
        "prev_close" => PrevClose,
        "market_cap" => MarketCap,
        "amplitude" => Amplitude,
        _ => null,
    };
}
