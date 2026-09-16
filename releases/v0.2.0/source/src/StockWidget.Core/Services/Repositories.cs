using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

// ---------------------------
// 自选股仓储
// ---------------------------
public interface IWatchlistRepository
{
    /// <summary>按显示顺序取全部自选股；表为空时写入默认列表。</summary>
    List<StockItem> GetAllOrSeed();
    List<StockItem> GetAll();
    /// <summary>添加（已存在返回 false）。name 可为空，首次抓取后回填。</summary>
    bool Add(string code, string name = "", bool pinned = false);
    bool Remove(string code);
    /// <summary>移动到目标位置（pinned 项不可移动，返回 false）。</summary>
    bool Move(string code, int targetSortOrder);
    void UpdateName(string code, string name);
    void UpdateGroup(string code, string group);
    void ReplaceAll(IEnumerable<StockItem> items);
    /// <summary>旧版固定股：sh000001 / sz399001。</summary>
    string[] PinnedCodes { get; }
}

public sealed class WatchlistRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IWatchlistRepository
{
    public string[] PinnedCodes => ["sh000001", "sz399001"];

    public List<StockItem> GetAllOrSeed()
    {
        using var db = dbFactory.CreateDbContext();
        if (!db.Watchlist.Any())
        {
            db.Watchlist.AddRange(
                new WatchlistEntity { Code = "sh000001", Name = "上证指数", SortOrder = 0, IsPinned = true },
                new WatchlistEntity { Code = "sz399001", Name = "深证成指", SortOrder = 1, IsPinned = true },
                new WatchlistEntity { Code = "sz399006", Name = "创业板指", SortOrder = 2 },
                new WatchlistEntity { Code = "sh588000", Name = "科创50ETF", SortOrder = 3 });
            db.SaveChanges();
        }

        return db.Watchlist
            .OrderBy(x => x.SortOrder)
            .AsEnumerable()
            .Select(x => x.ToModel())
            .ToList();
    }

    public List<StockItem> GetAll()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Watchlist
            .OrderBy(x => x.SortOrder)
            .AsEnumerable()
            .Select(x => x.ToModel())
            .ToList();
    }

    public bool Add(string code, string name = "", bool pinned = false)
    {
        using var db = dbFactory.CreateDbContext();
        if (db.Watchlist.Find(code) is not null) return false;
        var max = db.Watchlist.Any() ? db.Watchlist.Max(x => x.SortOrder) : -1;
        db.Watchlist.Add(new WatchlistEntity
        {
            Code = code,
            Name = name,
            SortOrder = max + 1,
            IsPinned = pinned,
        });
        db.SaveChanges();
        return true;
    }

    public bool Remove(string code)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.Watchlist.Find(code);
        if (row is null || row.IsPinned) return false;
        db.Watchlist.Remove(row);
        db.SaveChanges();
        return true;
    }

    public bool Move(string code, int targetSortOrder)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.Watchlist.Find(code);
        if (row is null || row.IsPinned) return false;

        var all = db.Watchlist.OrderBy(x => x.SortOrder).ToList();
        all.Remove(row);
        var idx = Math.Clamp(targetSortOrder, 0, all.Count);
        all.Insert(idx, row);
        for (var i = 0; i < all.Count; i++) all[i].SortOrder = i;
        db.SaveChanges();
        return true;
    }

    public void UpdateName(string code, string name)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.Watchlist.Find(code);
        if (row is null || row.Name == name) return;
        row.Name = name;
        db.SaveChanges();
    }

    public void UpdateGroup(string code, string group)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.Watchlist.Find(code);
        if (row is null || row.Group == group) return;
        row.Group = group;
        db.SaveChanges();
    }

    public void ReplaceAll(IEnumerable<StockItem> items)
    {
        using var db = dbFactory.CreateDbContext();
        db.Watchlist.RemoveRange(db.Watchlist);
        db.Watchlist.AddRange(items.Select(x => new WatchlistEntity
        {
            Code = x.Code,
            Name = x.Name,
            SortOrder = x.SortOrder,
            Group = x.Group,
            IsPinned = x.IsPinned,
        }));
        db.SaveChanges();
    }
}

// ---------------------------
// 每日成交额历史仓储（替代旧版 history_amount.json）
// ---------------------------
public interface IAmountHistoryRepository
{
    /// <summary>按日期覆盖写入（yyyy-MM-dd）。</summary>
    void Upsert(string date, decimal shAmount, decimal szAmount);
    /// <summary>当日覆盖写入（盘中每次刷新都覆盖，与旧版语义一致）。</summary>
    void UpsertToday(DateTime date, decimal shAmount, decimal szAmount) => Upsert(date.ToString("yyyy-MM-dd"), shAmount, szAmount);
    /// <summary>取严格早于指定日期的最近一条；无返回 null。</summary>
    DailyAmountEntity? GetLatestBefore(DateTime date);
    /// <summary>全部历史（升序）。</summary>
    List<DailyAmountEntity> GetAll();
    /// <summary>最近 N 个有记录的交易日（按日期升序返回）。</summary>
    List<DailyAmountEntity> GetRecent(int days);
    /// <summary>清理早于指定日期的记录，返回删除条数。</summary>
    int PruneOlderThan(DateTime date);
}

public sealed class AmountHistoryRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IAmountHistoryRepository
{
    public void Upsert(string date, decimal shAmount, decimal szAmount)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.DailyAmountHistory.Find(date);
        if (row is null)
        {
            db.DailyAmountHistory.Add(new DailyAmountEntity
            {
                Date = date,
                ShAmount = shAmount,
                SzAmount = szAmount,
                Total = shAmount + szAmount,
            });
        }
        else
        {
            row.ShAmount = shAmount;
            row.SzAmount = szAmount;
            row.Total = shAmount + szAmount;
        }
        db.SaveChanges();
    }

    public DailyAmountEntity? GetLatestBefore(DateTime date)
    {
        using var db = dbFactory.CreateDbContext();
        var key = date.ToString("yyyy-MM-dd");
        return db.DailyAmountHistory
            .Where(x => x.Date.CompareTo(key) < 0)
            .OrderByDescending(x => x.Date)
            .FirstOrDefault();
    }

    public List<DailyAmountEntity> GetAll()
    {
        using var db = dbFactory.CreateDbContext();
        return db.DailyAmountHistory.OrderBy(x => x.Date).ToList();
    }

    public List<DailyAmountEntity> GetRecent(int days)
    {
        using var db = dbFactory.CreateDbContext();
        return db.DailyAmountHistory.AsNoTracking()
            .OrderByDescending(d => d.Date)
            .Take(days)
            .OrderBy(d => d.Date)
            .ToList();
    }

    public int PruneOlderThan(DateTime date)
    {
        using var db = dbFactory.CreateDbContext();
        var key = date.ToString("yyyy-MM-dd");
        var old = db.DailyAmountHistory.Where(x => x.Date.CompareTo(key) < 0).ToList();
        db.DailyAmountHistory.RemoveRange(old);
        db.SaveChanges();
        return old.Count;
    }
}

// ---------------------------
// 当日行情快照仓储（迷你走势图 / 当日回看）
// ---------------------------
public interface IQuoteSnapshotRepository
{
    void AddRange(IEnumerable<QuoteData> quotes);
    /// <summary>取某只股票当日的快照序列（升序）。</summary>
    List<QuoteSnapshotEntity> GetTodayByCode(string code, DateTime date);
    /// <summary>删除非指定日期的快照（每天首刷时调用），返回删除条数。</summary>
    int PruneKeepDate(DateTime date);
}

public sealed class QuoteSnapshotRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IQuoteSnapshotRepository
{
    public void AddRange(IEnumerable<QuoteData> quotes)
    {
        using var db = dbFactory.CreateDbContext();
        db.QuoteSnapshots.AddRange(quotes
            .Where(q => q.Success && q.Price is not null)
            .Select(q => new QuoteSnapshotEntity
            {
                Code = q.Code,
                Time = q.FetchedAt,
                Price = q.Price,
                ChangePct = q.ChangePct,
            }));
        db.SaveChanges();
    }

    public List<QuoteSnapshotEntity> GetTodayByCode(string code, DateTime date)
    {
        using var db = dbFactory.CreateDbContext();
        var start = date.Date;
        return db.QuoteSnapshots
            .Where(x => x.Code == code && x.Time >= start && x.Time < start.AddDays(1))
            .OrderBy(x => x.Time)
            .ToList();
    }

    public int PruneKeepDate(DateTime date)
    {
        using var db = dbFactory.CreateDbContext();
        var keep = date.Date;
        var old = db.QuoteSnapshots.Where(x => x.Time < keep).Take(5000).ToList();
        db.QuoteSnapshots.RemoveRange(old);
        db.SaveChanges();
        return old.Count;
    }
}

// ---------------------------
// 预警规则仓储
// ---------------------------
public interface IAlertRepository
{
    List<AlertRuleEntity> GetAll();
    List<AlertRuleEntity> GetEnabled();
    AlertRuleEntity Add(string code, string name, decimal thresholdPct, int direction);
    void Update(AlertRuleEntity rule);
    void Remove(long id);
    void MarkTriggered(long id, DateTime time);
    /// <summary>某代码已有规则时返回 true。</summary>
    bool ExistsForCode(string code);
}

public sealed class AlertRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IAlertRepository
{
    public List<AlertRuleEntity> GetAll()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AlertRules.OrderBy(x => x.Code).ToList();
    }

    public List<AlertRuleEntity> GetEnabled()
    {
        using var db = dbFactory.CreateDbContext();
        return db.AlertRules.Where(x => x.Enabled).ToList();
    }

    public AlertRuleEntity Add(string code, string name, decimal thresholdPct, int direction)
    {
        using var db = dbFactory.CreateDbContext();
        var rule = new AlertRuleEntity
        {
            Code = code,
            StockName = name,
            ThresholdPct = thresholdPct,
            Direction = direction,
            Enabled = true,
        };
        db.AlertRules.Add(rule);
        db.SaveChanges();
        return rule;
    }

    public void Update(AlertRuleEntity rule)
    {
        using var db = dbFactory.CreateDbContext();
        db.AlertRules.Update(rule);
        db.SaveChanges();
    }

    public void Remove(long id)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.AlertRules.Find(id);
        if (row is null) return;
        db.AlertRules.Remove(row);
        db.SaveChanges();
    }

    public void MarkTriggered(long id, DateTime time)
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.AlertRules.Find(id);
        if (row is null) return;
        row.LastTriggeredAt = time;
        db.SaveChanges();
    }

    public bool ExistsForCode(string code)
    {
        using var db = dbFactory.CreateDbContext();
        return db.AlertRules.Any(x => x.Code == code);
    }
}

// ---------------------------
// 日K线仓储（按 Code+Date 联合主键）
// ---------------------------
public interface IDailyKlineRepository
{
    /// <summary>批量 upsert（按 Code+Date 存在则覆盖）。</summary>
    void UpsertRange(IEnumerable<DailyKlineEntity> klines);

    /// <summary>今日（DateTime.Today）已归档的代码集合。</summary>
    HashSet<string> GetArchivedCodesToday();

    /// <summary>某代码最近 N 根日K，按日期升序返回。</summary>
    List<DailyKlineEntity> GetByCode(string code, int days);
}

public sealed class DailyKlineRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IDailyKlineRepository
{
    public void UpsertRange(IEnumerable<DailyKlineEntity> klines)
    {
        var list = klines.ToList();
        if (list.Count == 0) return;
        using var db = dbFactory.CreateDbContext();
        var codes = list.Select(k => k.Code).Distinct().ToList();
        var dates = list.Select(k => k.Date).Distinct().ToList();
        // 按涉及范围粗筛后在内存中精确匹配（避免 EF 无法翻译组合键 Contains）
        var existing = db.DailyKlines
            .Where(k => codes.Contains(k.Code) && dates.Contains(k.Date))
            .AsEnumerable()
            .Where(k => list.Any(x => x.Code == k.Code && x.Date == k.Date))
            .ToDictionary(k => (k.Code, k.Date));
        foreach (var k in list)
        {
            if (existing.TryGetValue((k.Code, k.Date), out var row))
            {
                row.Open = k.Open; row.High = k.High; row.Low = k.Low; row.Close = k.Close;
                row.Volume = k.Volume; row.Amount = k.Amount;
            }
            else
            {
                db.DailyKlines.Add(k);
            }
        }
        db.SaveChanges();
    }

    public HashSet<string> GetArchivedCodesToday()
    {
        using var db = dbFactory.CreateDbContext();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return db.DailyKlines.Where(k => k.Date == today)
            .Select(k => k.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public List<DailyKlineEntity> GetByCode(string code, int days)
    {
        using var db = dbFactory.CreateDbContext();
        return db.DailyKlines.AsNoTracking()
            .Where(k => k.Code == code)
            .OrderByDescending(k => k.Date)
            .Take(days)
            .OrderBy(k => k.Date)
            .ToList();
    }
}
