using StockWidget.Core.Models;

namespace StockWidget.Core.Data.Entities;

/// <summary>settings 表：应用设置（key-value，value 为 JSON）。</summary>
public sealed class SettingEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>watchlist 表：自选股。</summary>
public sealed class WatchlistEntity
{
    /// <summary>规范化代码，如 sh600390。</summary>
    public string Code { get; set; } = "";

    /// <summary>名称缓存（首次抓取后更新）。</summary>
    public string Name { get; set; } = "";

    /// <summary>显示顺序（小在前）。</summary>
    public int SortOrder { get; set; }

    /// <summary>分组名（空 = 未分组，按分类自动分组时忽略此字段）。</summary>
    public string Group { get; set; } = "";

    /// <summary>固定股（旧版 fixed_stocks：不可删除 / 不可移动）。</summary>
    public bool IsPinned { get; set; }
}

/// <summary>daily_amount_history 表：每日沪深两市成交额（亿）。</summary>
public sealed class DailyAmountEntity
{
    /// <summary>日期，yyyy-MM-dd。</summary>
    public string Date { get; set; } = "";

    public decimal ShAmount { get; set; }

    public decimal SzAmount { get; set; }

    public decimal Total { get; set; }
}

/// <summary>quote_snapshots 表：当日行情快照（滚动保留，供迷你走势图 / 当日回看）。</summary>
public sealed class QuoteSnapshotEntity
{
    public long Id { get; set; }

    public string Code { get; set; } = "";

    public DateTime Time { get; set; }

    public decimal? Price { get; set; }

    public decimal? ChangePct { get; set; }
}

/// <summary>alert_rules 表：涨跌幅预警规则。</summary>
public sealed class AlertRuleEntity
{
    public long Id { get; set; }

    public string Code { get; set; } = "";

    /// <summary>名称缓存（展示用）。</summary>
    public string StockName { get; set; } = "";

    /// <summary>阈值（涨跌幅绝对值，%）。</summary>
    public decimal ThresholdPct { get; set; }

    /// <summary>触发方向：0 = 任意（涨或跌都触发），1 = 仅涨，-1 = 仅跌。</summary>
    public int Direction { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>每次刷新触发一次防抖：最近一次触发时间（同一交易日同一方向只提示一次）。</summary>
    public DateTime? LastTriggeredAt { get; set; }
}

/// <summary>预警方向常量。</summary>
public static class AlertDirection
{
    public const int Any = 0;
    public const int RiseOnly = 1;
    public const int FallOnly = -1;
}

/// <summary>实体 → 领域模型映射扩展。</summary>
public static class EntityExtensions
{
    public static StockItem ToModel(this WatchlistEntity e) => new()
    {
        Code = e.Code,
        Name = e.Name,
        SortOrder = e.SortOrder,
        Group = e.Group,
        IsPinned = e.IsPinned,
    };
}

/// <summary>trading_calendar 表：交易日历（含调休上班的周末）。</summary>
public sealed class TradingCalendarEntity
{
    /// <summary>日期，yyyy-MM-dd。（主键）</summary>
    public string Date { get; set; } = "";

    /// <summary>true=交易日（含调休上班的周末）；false=休市日（法定节假日）。</summary>
    public bool IsTradingDay { get; set; }

    /// <summary>备注（如"春节假期" / "调休上班"）。</summary>
    public string Remark { get; set; } = "";
}

/// <summary>daily_kline 表：日K线（每股每日一根，收盘归档）。</summary>
public sealed class DailyKlineEntity
{
    /// <summary>规范化代码（联合主键）。</summary>
    public string Code { get; set; } = "";

    /// <summary>交易日期 yyyy-MM-dd（联合主键）。</summary>
    public string Date { get; set; } = "";

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }

    /// <summary>成交量（手，腾讯口径）。</summary>
    public decimal Volume { get; set; }

    /// <summary>成交额（万元，腾讯口径）。</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// AI 分析结果缓存（v0.3.0）：Key = 代码 + 最后K线日 + 最新收盘 + 模型；
/// 同一行情数据命中缓存直接展示，避免重复消耗 API 额度。不含 API Key。
/// </summary>
public sealed class AiAnalysisCacheEntity
{
    /// <summary>缓存键（主键）。</summary>
    public string Key { get; set; } = "";

    /// <summary>分析结果 JSON（AiMarketAnalysisResult 序列化）。</summary>
    public string ResultJson { get; set; } = "";

    /// <summary>生成时间 yyyy-MM-dd HH:mm:ss。</summary>
    public string CreatedAt { get; set; } = "";
}
