using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class DailyKlineRepositoryTests : DatabaseTestBase
{
    private static DailyKlineEntity K(string code, string date, decimal close) =>
        new() { Code = code, Date = date, Open = close, High = close, Low = close, Close = close, Volume = 100, Amount = 50 };

    [Fact]
    public void UpsertRange_IsIdempotent()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("sh600390", "2026-09-16", 10m)]);
        repo.UpsertRange([K("sh600390", "2026-09-16", 11m)]); // 同键覆盖
        var all = repo.GetByCode("sh600390", 10);
        Assert.Single(all);
        Assert.Equal(11m, all[0].Close);
    }

    [Fact]
    public void GetByCode_ReturnsAscendingLatestN()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("sh600390", "2026-09-14", 10m), K("sh600390", "2026-09-15", 11m), K("sh600390", "2026-09-16", 12m)]);
        var rows = repo.GetByCode("sh600390", 2);
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-09-15", rows[0].Date); // 升序返回
        Assert.Equal("2026-09-16", rows[1].Date);
        Assert.Empty(repo.GetByCode("sz000001", 10));
    }

    [Fact]
    public void GetArchivedCodesToday_ReturnsTodayOnly()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        repo.UpsertRange([K("sh600390", today, 10m), K("sh600390", "2026-01-01", 10m)]);
        var codes = repo.GetArchivedCodesToday();
        Assert.Contains("sh600390", codes);
        Assert.Single(codes);
    }

    [Fact]
    public void InsertMissing_InsertsOnlyAbsentRows_AndNeverOverwrites()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("sh600390", "2026-09-15", 10m)]);

        var inserted = repo.InsertMissing(
        [
            K("sh600390", "2026-09-15", 99m), // 已存在：不覆盖
            K("sh600390", "2026-09-16", 11m), // 缺失：插入
            K("sz000001", "2026-09-16", 5m),  // 其他代码缺失：插入
        ]);

        Assert.Equal(2, inserted);
        var rows = repo.GetByCode("sh600390", 10);
        Assert.Equal(2, rows.Count);
        Assert.Equal(10m, rows.Single(r => r.Date == "2026-09-15").Close); // 原值保留
    }

    [Fact]
    public void GetLatestDate_ReturnsNewestOrNull()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        Assert.Null(repo.GetLatestDate("sh600390"));

        repo.UpsertRange([K("sh600390", "2026-09-15", 10m), K("sh600390", "2026-09-16", 11m)]);
        Assert.Equal("2026-09-16", repo.GetLatestDate("sh600390"));
        Assert.Null(repo.GetLatestDate("sz000001"));
    }
}
