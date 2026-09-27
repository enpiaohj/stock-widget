using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class TradingCalendarServiceTests
{
    // 用一次性种子字典构造（判定服务吞入日历 + 系统时间提供者，不依赖真实数据库）
    private static readonly Dictionary<string, bool> Cal = new()
    {
        // 仅验判定逻辑，与实际内置表解耦
        ["2026-02-20"] = true,   // 调休上班·周五（交易日）
        ["2026-01-01"] = false,  // 元旦（工作日·休市）
        ["2026-02-21"] = false,  // 春节初四假期（周六·休市，但表内明确标注）
    };

    private static TradingCalendarService Make() =>
        new(Cal);

    [Fact]
    public void WorkingDay_InCalendarFalse_Closed()
    {
        // 2026-01-01 是周四；表内 false → 休市
        Assert.False(Make().IsTradingDay(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void Weekday_InCalendarTrue_Open()
    {
        // 2026-02-20 是周五；表内 true → 交易日（调休上班）
        Assert.True(Make().IsTradingDay(new DateTime(2026, 2, 20)));
    }

    [Fact]
    public void NormalWeekend_Fallback_Closed()
    {
        // 2026-06-13 是周六，未命中 → 周末休市
        Assert.False(Make().IsTradingDay(new DateTime(2026, 6, 13)));
    }

    [Fact]
    public void NormalWeekday_Fallback_Open()
    {
        // 2026-06-15 是周一，未命中 → 工作日开盘
        Assert.True(Make().IsTradingDay(new DateTime(2026, 6, 15)));
    }

    [Fact]
    public void IsTodayTrading_UsesCurrentSystemTime()
    {
        // 隐式用 DateTime.Now；单测只保证可调用且返回与 IsTradingDay 一致的布尔
        var svc = Make();
        Assert.Equal(svc.IsTradingDay(DateTime.Today), svc.IsTodayTrading());
    }
}