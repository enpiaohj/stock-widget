using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class BackupServiceTests : DatabaseTestBase
{
    [Fact]
    public async Task ExportThenImport_RoundTripsData()
    {
        var path = Path.Combine(TempDirectory, "backup.json");

        // 准备源库数据
        var svc = Provider.GetRequiredService<IBackupService>();
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.FontSize = 14;
        settings.Save(cfg);

        var result = await svc.ExportAsync(path);
        Assert.True(result.Success, result.Message);
        Assert.True(File.Exists(path));

        // 目标库（全新容器）导入
        var targetDir = Path.Combine(TempDirectory, "target");
        Directory.CreateDirectory(targetDir);
        var targetServices = new ServiceCollection()
            .AddStockWidgetCore(Path.Combine(targetDir, "t.db"))
            .BuildServiceProvider();
        var target = targetServices.GetRequiredService<IBackupService>();

        var import = await target.ImportAsync(path);
        Assert.True(import.Success, import.Message);

        var targetSettings = targetServices.GetRequiredService<ISettingsService>();
        Assert.Equal(14, targetSettings.Current.FontSize);
    }

    [Fact]
    public async Task Import_InvalidFile_Fails()
    {
        var path = Path.Combine(TempDirectory, "bad.json");
        await File.WriteAllTextAsync(path, "{ not a backup }");
        var svc = Provider.GetRequiredService<IBackupService>();
        var result = await svc.ImportAsync(path);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Import_MergesWithoutDuplicates()
    {
        var path = Path.Combine(TempDirectory, "merge.json");

        // 源库：2 只自选股、1 天历史、1 条预警
        var srcDb = Provider.GetRequiredService<IDbContextFactory<StockWidgetDbContext>>();
        await using (var db = await srcDb.CreateDbContextAsync())
        {
            db.Watchlist.AddRange(
                new WatchlistEntity { Code = "sh600000", Name = "浦发银行", SortOrder = 0 },
                new WatchlistEntity { Code = "sh600519", Name = "贵州茅台", SortOrder = 1 });
            db.DailyAmountHistory.Add(new DailyAmountEntity
                { Date = "2026-09-01", ShAmount = 3000m, SzAmount = 4000m, Total = 7000m });
            db.AlertRules.Add(new AlertRuleEntity { Code = "sh600000", ThresholdPct = 5m });
            await db.SaveChangesAsync();
        }

        var export = await Provider.GetRequiredService<IBackupService>().ExportAsync(path);
        Assert.True(export.Success, export.Message);

        // 目标库：已含同名自选股 / 同日期历史 / 同代码+方向预警（均应被跳过或更新而非重复）
        var targetDir = Path.Combine(TempDirectory, "target2");
        Directory.CreateDirectory(targetDir);
        var targetServices = new ServiceCollection()
            .AddStockWidgetCore(Path.Combine(targetDir, "t.db"))
            .BuildServiceProvider();
        var targetDb = targetServices.GetRequiredService<IDbContextFactory<StockWidgetDbContext>>();
        await using (var db = await targetDb.CreateDbContextAsync())
        {
            db.Watchlist.Add(new WatchlistEntity { Code = "SH600000", Name = "已有", SortOrder = 0 });
            db.DailyAmountHistory.Add(new DailyAmountEntity
                { Date = "2026-09-01", ShAmount = 1m, SzAmount = 1m, Total = 2m });
            db.AlertRules.Add(new AlertRuleEntity { Code = "sh600000", ThresholdPct = 3m });
            await db.SaveChangesAsync();
        }

        var import = await targetServices.GetRequiredService<IBackupService>().ImportAsync(path);
        Assert.True(import.Success, import.Message);
        Assert.Equal(1, import.Watchlist); // 仅 sh600519 新增
        Assert.Equal(1, import.DailyDays); // 同日期为更新（计入更新数）
        Assert.Equal(0, import.AlertRules); // 同代码+方向跳过

        await using (var db = await targetDb.CreateDbContextAsync())
        {
            var wl = await db.Watchlist.OrderBy(w => w.SortOrder).ToListAsync();
            Assert.Equal(2, wl.Count);
            Assert.Equal(["SH600000", "sh600519"], wl.Select(w => w.Code));

            var days = await db.DailyAmountHistory.ToListAsync();
            var day = Assert.Single(days);
            Assert.Equal(7000m, day.Total); // 被备份值覆盖

            var alerts = await db.AlertRules.ToListAsync();
            var alert = Assert.Single(alerts);
            Assert.Equal(3m, alert.ThresholdPct); // 保留目标库已有规则
            Assert.Null(alert.LastTriggeredAt);
        }
    }
}
