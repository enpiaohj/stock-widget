using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data.Entities;

namespace StockWidget.Core.Data;

/// <summary>SQLite 数据库上下文（EF Core，Code-First 迁移）。</summary>
public sealed class StockWidgetDbContext(DbContextOptions<StockWidgetDbContext> options) : DbContext(options)
{
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();
    public DbSet<WatchlistEntity> Watchlist => Set<WatchlistEntity>();
    public DbSet<DailyAmountEntity> DailyAmountHistory => Set<DailyAmountEntity>();
    public DbSet<QuoteSnapshotEntity> QuoteSnapshots => Set<QuoteSnapshotEntity>();
    public DbSet<AlertRuleEntity> AlertRules => Set<AlertRuleEntity>();
    public DbSet<TradingCalendarEntity> TradingCalendar => Set<TradingCalendarEntity>();
    public DbSet<DailyKlineEntity> DailyKlines => Set<DailyKlineEntity>();
    public DbSet<AiAnalysisCacheEntity> AiAnalysisCaches => Set<AiAnalysisCacheEntity>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<SettingEntity>(e =>
        {
            e.ToTable("settings");
            e.HasKey(x => x.Key);
        });

        mb.Entity<WatchlistEntity>(e =>
        {
            e.ToTable("watchlist");
            e.HasKey(x => x.Code);
            e.Property(x => x.Name).HasDefaultValue("");
            e.Property(x => x.Group).HasDefaultValue("");
            e.HasIndex(x => x.SortOrder);
        });

        mb.Entity<DailyAmountEntity>(e =>
        {
            e.ToTable("daily_amount_history");
            e.HasKey(x => x.Date);
        });

        mb.Entity<QuoteSnapshotEntity>(e =>
        {
            e.ToTable("quote_snapshots");
            e.HasIndex(x => new { x.Code, x.Time });
        });

        mb.Entity<AlertRuleEntity>(e =>
        {
            e.ToTable("alert_rules");
            e.HasIndex(x => x.Code);
        });

        mb.Entity<TradingCalendarEntity>(e =>
        {
            e.ToTable("trading_calendar");
            e.HasKey(x => x.Date);
            e.Property(x => x.Remark).HasDefaultValue("");
        });

        mb.Entity<DailyKlineEntity>(e =>
        {
            e.ToTable("daily_kline");
            e.HasKey(x => new { x.Code, x.Date });
        });

        mb.Entity<AiAnalysisCacheEntity>(e =>
        {
            e.ToTable("ai_analysis_cache");
            e.HasKey(x => x.Key);
        });
    }
}
