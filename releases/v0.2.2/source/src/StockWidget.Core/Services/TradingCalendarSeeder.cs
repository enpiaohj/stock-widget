using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;

namespace StockWidget.Core.Services;

/// <summary>内置中国 A 股市场交易日历种子数据（仅存放"例外"，未覆盖的日期走默认规则：周末休市、工作日开盘）。</summary>
public static class TradingCalendarSeeder
{
    /// <summary>date(yyyy-MM-dd) → 是否交易日（true=交易）。未覆盖 = 默认（周末休市、工作日开盘）。</summary>
    public static IReadOnlyDictionary<string, bool> GetBuiltIn()
    {
        var m = new Dictionary<string, bool>(StringComparer.Ordinal);
        Seed2025(m);
        Seed2026(m);
        return m;
    }

    /// <summary>库中该表为空时写入内置数据（非覆盖，保证可维护）。</summary>
    public static void SeedIfEmpty(StockWidgetDbContext db)
    {
        if (db.TradingCalendar.Any()) return;
        foreach (var (date, isTrading) in GetBuiltIn())
        {
            db.TradingCalendar.Add(new TradingCalendarEntity
            {
                Date = date,
                IsTradingDay = isTrading,
                Remark = isTrading ? "调休上班" : "法定节假日",
            });
        }
        db.SaveChanges();
    }

    // 依据国务院办公厅 2025 年节假日安排（官方已成文可查）。
    private static void Seed2025(Dictionary<string, bool> m)
    {
        // 元旦
        Add(m, "2025-01-01", false);

        // 春节 1/28–2/4 放假（含除夕）；调休上班 1/26(周日)、2/8(周六)
        Add(m, "2025-01-26", true);
        Add(m, "2025-01-28", false);
        Add(m, "2025-01-29", false);
        Add(m, "2025-01-30", false);
        Add(m, "2025-01-31", false);
        Add(m, "2025-02-01", false);
        Add(m, "2025-02-02", false);
        Add(m, "2025-02-03", false);
        Add(m, "2025-02-04", false);
        Add(m, "2025-02-08", true);

        // 清明 4/4–4/6
        Add(m, "2025-04-04", false);
        Add(m, "2025-04-05", false);
        Add(m, "2025-04-06", false);

        // 劳动节 5/1–5/5；调休上班 4/27(周日)
        Add(m, "2025-04-27", true);
        Add(m, "2025-05-01", false);
        Add(m, "2025-05-02", false);
        Add(m, "2025-05-03", false);
        Add(m, "2025-05-04", false);
        Add(m, "2025-05-05", false);

        // 端午 5/31–6/2
        Add(m, "2025-05-31", false);
        Add(m, "2025-06-01", false);
        Add(m, "2025-06-02", false);

        // 中秋+国庆 10/1–10/8；调休上班 9/28(周日)、10/11(周六)
        Add(m, "2025-09-28", true);
        Add(m, "2025-10-01", false);
        Add(m, "2025-10-02", false);
        Add(m, "2025-10-03", false);
        Add(m, "2025-10-04", false);
        Add(m, "2025-10-05", false);
        Add(m, "2025-10-06", false);
        Add(m, "2025-10-07", false);
        Add(m, "2025-10-08", false);
        Add(m, "2025-10-11", true);
    }

    // 2026 采用保守策略：仅收录无需依赖国务院通知即可确定的固定节假日。
    private static void Seed2026(Dictionary<string, bool> m)
    {
        // 元旦（1/1 为固定法定假日，市场必休——不依赖当年调休方案）
        Add(m, "2026-01-01", false);
    }

    private static void Add(Dictionary<string, bool> m, string date, bool isTrading) => m[date] = isTrading;
}