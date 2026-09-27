using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class AmountHistoryRepositoryTests : DatabaseTestBase
{
    [Fact]
    public void GetRecent_ReturnsAscendingLatestN()
    {
        var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
        var today = DateTime.Today;
        repo.UpsertToday(today.AddDays(-2), 100m, 200m);
        repo.UpsertToday(today.AddDays(-1), 110m, 220m);
        repo.UpsertToday(today, 120m, 240m);

        var rows = repo.GetRecent(2);
        Assert.Equal(2, rows.Count);                       // 最近 2 天
        Assert.Equal(today.AddDays(-1).ToString("yyyy-MM-dd"), rows[0].Date); // 升序
        Assert.Equal(today.ToString("yyyy-MM-dd"), rows[1].Date);
        Assert.Equal(120m + 240m, rows[1].Total);
    }

    [Fact]
    public void GetRecent_EmptyDb_ReturnsEmpty()
    {
        var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
        Assert.Empty(repo.GetRecent(60));
    }
}
