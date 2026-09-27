using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class KlineArchiverTests : DatabaseTestBase
{
    // QuoteData 没有 Close 字段：收盘价来源是 Price（现价）。
    private static QuoteData Q(string code, decimal close) => new()
    {
        Code = code, Name = "测试", Price = close, Open = close, High = close, Low = close,
        Volume = 1000, Amount = 500, Success = true,
    };

    private KlineArchiver MakeArchiver() =>
        new(Provider.GetRequiredService<IDailyKlineRepository>(),
            new TradingCalendarService(TradingCalendarSeeder.GetBuiltIn()));

    [Fact]
    public void TradingDay_After1505_ArchivesAllSuccessQuotes()
    {
        var now = new DateTime(2026, 9, 16, 15, 6, 0); // 周三（兜底交易日）
        MakeArchiver().TryArchive([Q("sh600390", 10m), QuoteData.Failed("sz000001")], now);

        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        var rows = repo.GetByCode("sh600390", 5);
        Assert.Single(rows);            // 失败占位不归档
        Assert.Equal(10m, rows[0].Close);
        Assert.Empty(repo.GetByCode("sz000001", 5));
    }

    [Fact]
    public void Before1505_Skips()
    {
        var now = new DateTime(2026, 9, 16, 14, 0, 0);
        MakeArchiver().TryArchive([Q("sh600390", 10m)], now);
        Assert.Empty(Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5));
    }

    [Fact]
    public void NonTradingDay_Skips()
    {
        var now = new DateTime(2026, 9, 19, 15, 6, 0); // 周六（空日历兜底为非交易日）
        MakeArchiver().TryArchive([Q("sh600390", 10m)], now);
        Assert.Empty(Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5));
    }

    [Fact]
    public void ReArchive_SameDay_OverwritesNotDuplicates()
    {
        var now = new DateTime(2026, 9, 16, 15, 6, 0);
        MakeArchiver().TryArchive([Q("sh600390", 10m)], now);
        MakeArchiver().TryArchive([Q("sh600390", 11m)], now); // 二次归档覆盖
        var rows = Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5);
        Assert.Single(rows);
        Assert.Equal(11m, rows[0].Close);
    }
}
