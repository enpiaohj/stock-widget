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
