using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Services;
using Xunit;

namespace StockWidget.Tests;

/// <summary>仓储层验证（临时 SQLite 数据库）。</summary>
public class RepositoryTests : DatabaseTestBase
{
    [Fact]
    public void Watchlist_SeedsDefaultOnFirstAccess()
    {
        var repo = Provider.GetRequiredService<IWatchlistRepository>();
        var all = repo.GetAllOrSeed();

        Assert.Equal(4, all.Count);
        Assert.Equal("sh000001", all[0].Code);
        Assert.True(all[0].IsPinned);
        Assert.False(repo.Remove("sh000001")); // 固定股不可删
    }

    [Fact]
    public void Watchlist_AddDuplicate_ReturnsFalse()
    {
        var repo = Provider.GetRequiredService<IWatchlistRepository>();
        repo.GetAllOrSeed();

        Assert.True(repo.Add("sh600390"));
        Assert.False(repo.Add("sh600390"));
        Assert.True(repo.Remove("sh600390"));
        Assert.False(repo.Remove("sh600390"));
    }

    [Fact]
    public void Watchlist_MovePinned_ReturnsFalse()
    {
        var repo = Provider.GetRequiredService<IWatchlistRepository>();
        repo.GetAllOrSeed();

        Assert.False(repo.Move("sh000001", 3)); // pinned 不可移动
        Assert.True(repo.Move("sz399006", 0));
        var all = repo.GetAll();
        Assert.Equal("sz399006", all[0].Code);
    }

    [Fact]
    public void AmountHistory_UpsertOverwritesSameDate()
    {
        var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
        var today = new DateTime(2026, 9, 15);

        repo.UpsertToday(today, 8000m, 9000m);
        repo.UpsertToday(today, 8100m, 9100m); // 盘中覆盖

        var all = repo.GetAll();
        var row = Assert.Single(all);
        Assert.Equal(8100m, row.ShAmount);
        Assert.Equal(17200m, row.Total);
    }

    [Fact]
    public void AmountHistory_GetLatestBefore_SkipsToday()
    {
        var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
        repo.Upsert("2026-09-10", 7000m, 8000m);
        repo.Upsert("2026-09-11", 7100m, 8100m);
        repo.Upsert("2026-09-14", 8000m, 9000m);

        var latest = repo.GetLatestBefore(new DateTime(2026, 9, 15));
        Assert.NotNull(latest);
        Assert.Equal("2026-09-14", latest!.Date); // 取今天之前最近一天
        Assert.Equal(17000m, latest.Total);

        Assert.Null(repo.GetLatestBefore(new DateTime(2026, 9, 10))); // 严格早于
    }

    [Fact]
    public void AmountHistory_PruneOlderThan()
    {
        var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
        repo.Upsert("2024-01-01", 1m, 1m);
        repo.Upsert("2026-09-14", 2m, 2m);

        var removed = repo.PruneOlderThan(new DateTime(2026, 9, 15).AddYears(-2)); // 2024-09-15

        Assert.Equal(1, removed);
        Assert.Single(repo.GetAll());
    }

    [Fact]
    public void Settings_SaveAndReload_Roundtrip()
    {
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.Theme = "light";
        cfg.Hotkey = "alt+z";
        cfg.FieldWidths["price"] = 20;
        settings.Save(cfg);

        var reloaded = settings.Reload();
        Assert.Equal("light", reloaded.Theme);
        Assert.Equal("alt+z", reloaded.Hotkey);
        Assert.Equal(20, reloaded.FieldWidths["price"]);
    }

    [Fact]
    public void Settings_MissingFieldWidths_AutoFilled()
    {
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.FieldWidths.Clear();
        settings.Save(cfg);

        var reloaded = settings.Reload();
        foreach (var (_, key, w, _) in FieldDefinitions.All)
        {
            Assert.True(reloaded.FieldWidths.ContainsKey(key));
            Assert.Equal(w, reloaded.FieldWidths[key]);
        }
    }
}


public class WatchlistReorderAllTests : DatabaseTestBase
{
    [Fact]
    public void ReorderAll_RewritesSortOrderToMatchGivenSequence()
    {
        var repo = Provider.GetRequiredService<IWatchlistRepository>();
        Assert.True(repo.Add("sh600390", "A"));
        Assert.True(repo.Add("sz000001", "B"));
        Assert.True(repo.Add("sh510050", "C"));

        repo.ReorderAll(["sh510050", "sh600390", "sz000001"]);

        var all = repo.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Equal("sh510050", all[0].Code);
        Assert.Equal("sh600390", all[1].Code);
        Assert.Equal("sz000001", all[2].Code);
        Assert.Equal(0, all[0].SortOrder);
        Assert.Equal(1, all[1].SortOrder);
        Assert.Equal(2, all[2].SortOrder);
    }

    [Fact]
    public void ReorderAll_KeepsRowsNotInList()
    {
        var repo = Provider.GetRequiredService<IWatchlistRepository>();
        repo.Add("sh600390", "A");
        repo.Add("sz000001", "B");
        repo.ReorderAll(["sz000001"]);
        Assert.Equal(2, repo.GetAll().Count);
    }
}
