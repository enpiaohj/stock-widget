namespace StockWidget.Core.Services;

/// <summary>交易日判定：命中内置/维护日历 → 按表；未命中 → 周末休市、工作日开盘兜底。</summary>
public interface ITradingCalendar
{
    /// <summary>指定日是否为交易日（非交易日 → 休市）。</summary>
    bool IsTradingDay(DateTime date);
    /// <summary>今天是否为交易日（使用当前系统时间）。</summary>
    bool IsTodayTrading();
}

/// <summary>实现：按日历字典判定，未命中按周末/工作日兜底，永不抛异常。</summary>
public sealed class TradingCalendarService(
    IReadOnlyDictionary<string, bool> calendar) : ITradingCalendar
{
    private (DateTime Day, bool IsTrading)? _todayCache;

    public bool IsTradingDay(DateTime date)
    {
        if (calendar.TryGetValue(date.ToString("yyyy-MM-dd"), out var isTrading))
            return isTrading;

        // 兜底：周末休市、工作日开盘（保证永远有结果，不抛异常）
        return date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    public bool IsTodayTrading()
    {
        var day = DateTime.Today;
        if (_todayCache is { } c && c.Day == day) return c.IsTrading;
        var result = IsTradingDay(day);
        _todayCache = (day, result);
        return result;
    }
}