using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data;
using StockWidget.Core.Services;

namespace StockWidget.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>注册 Core 层全部服务。dbPath 用于测试注入内存库等场景。</summary>
    public static IServiceCollection AddStockWidgetCore(this IServiceCollection services, string? dbPath = null)
    {
        var path = dbPath ?? DbPathResolver.GetDatabasePath();
        services.AddDbContextFactory<StockWidgetDbContext>(o => o.UseSqlite($"Data Source={path}"));

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWatchlistRepository, WatchlistRepository>();
        services.AddSingleton<IAmountHistoryRepository, AmountHistoryRepository>();
        services.AddSingleton<IQuoteSnapshotRepository, QuoteSnapshotRepository>();
        services.AddSingleton<IDailyKlineRepository, DailyKlineRepository>();
        services.AddSingleton<IAlertRepository, AlertRepository>();
        services.AddSingleton<ILegacyImporter, LegacyImporter>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<ITencentQuoteApi, TencentQuoteApi>();
        services.AddSingleton<IPriceRefreshService, PriceRefreshService>();

        // 交易日历：内置种子 + 兜底周末规则
        var builtInCal = TradingCalendarSeeder.GetBuiltIn();
        services.AddSingleton<ITradingCalendar>(new TradingCalendarService(builtInCal));
        services.AddSingleton<IKlineArchiver, KlineArchiver>();

        // 首次运行：建库 / 迁移
        using (var db = new StockWidgetDbContext(new DbContextOptionsBuilder<StockWidgetDbContext>()
                   .UseSqlite($"Data Source={path}").Options))
        {
            db.Database.Migrate();

            // 空库时写入内置交易日历种子
            using (var seed = new StockWidgetDbContext(new DbContextOptionsBuilder<StockWidgetDbContext>()
                       .UseSqlite($"Data Source={path}").Options))
            {
                TradingCalendarSeeder.SeedIfEmpty(seed);
            }
        }

        return services;
    }
}
