using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

// ---------------------------
// 刷新编排：抓取 → 合并旧值 → 量能统计 → 快照入库 → 预警评估
// ---------------------------
public sealed record RefreshResult
{
    /// <summary>与输入自选股顺序对应；抓取失败的条目保留旧值或为 Failed 占位。</summary>
    public required List<QuoteData> Quotes { get; init; }

    /// <summary>沪深两市总成交额（亿）。两大指数未全部抓到时为 null（本轮不计）。</summary>
    public decimal? TotalAmountYi { get; init; }

    /// <summary>上一交易日总成交额（亿）；无历史时为 null。</summary>
    public decimal? YesterdayTotalYi { get; init; }

    /// <summary>今日 - 上一交易日（亿）；无历史时为 null。</summary>
    public decimal? AmountDiff { get; init; }

    /// <summary>本轮触发预警的时间。</summary>
    public DateTime RefreshedAt { get; init; } = DateTime.Now;

    /// <summary>本轮触发的预警（供 UI 弹通知）。</summary>
    public List<AlertTrigger> TriggeredAlerts { get; init; } = [];
}

/// <summary>一次预警触发。</summary>
public sealed record AlertTrigger(
    string Code,
    string Name,
    decimal ChangePct,
    decimal ThresholdPct,
    int Direction);

public interface IPriceRefreshService
{
    Task<RefreshResult> RefreshAsync(
        IReadOnlyList<StockItem> watchlist,
        IReadOnlyList<QuoteData> prevQuotes,
        CancellationToken ct = default);
}

/// <summary>旧版口径：沪深两大指数。</summary>
public static class MainIndexCodes
{
    public const string Shanghai = "sh000001";
    public const string Shenzhen = "sz399001";
}

public sealed class PriceRefreshService(
    ITencentQuoteApi api,
    IAmountHistoryRepository amountHistory,
    IQuoteSnapshotRepository snapshots,
    IWatchlistRepository watchlistRepo,
    IAlertRepository alerts,
    ISettingsService settings) : IPriceRefreshService
{
    private static DateTime _lastAmountPruneDate = DateTime.MinValue;
    private static readonly object PruneLock = new();

    public async Task<RefreshResult> RefreshAsync(
        IReadOnlyList<StockItem> watchlist,
        IReadOnlyList<QuoteData> prevQuotes,
        CancellationToken ct = default)
    {
        var codes = watchlist.Select(w => w.Code).ToList();
        var fetched = await api.FetchQuotesAsync(codes, ct).ConfigureAwait(false);
        var byCode = fetched.Where(f => f.Success)
            .GroupBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var prevByCode = prevQuotes
            .GroupBy(q => q.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        // 合并：失败保留旧值；无旧值则失败占位（UI 展示 "-"/灰）
        var merged = watchlist.Select(w =>
            byCode.TryGetValue(w.Code, out var q) ? q
                : prevByCode.TryGetValue(w.Code, out var p) ? p
                : QuoteData.Failed(w.Code)
        ).ToList();

        // 回填自选股名称缓存
        foreach (var q in byCode.Values)
            watchlistRepo.UpdateName(q.Code, q.Name);

        // 沪深量能：两大指数成交额（万元）÷10000 = 亿；只有都抓到才入当日记录
        decimal? totalYi = null;
        var sh = byCode.GetValueOrDefault(MainIndexCodes.Shanghai);
        var sz = byCode.GetValueOrDefault(MainIndexCodes.Shenzhen);
        if (sh?.Amount is not null && sz?.Amount is not null)
        {
            var shYi = Math.Round(sh.Amount.Value / 10000m, 2);
            var szYi = Math.Round(sz.Amount.Value / 10000m, 2);
            totalYi = shYi + szYi;
            amountHistory.UpsertToday(DateTime.Now, shYi, szYi);
            MaintenancePrune();
        }

        // 与上一交易日对比
        decimal? yesterdayYi = null;
        if (totalYi is not null)
        {
            var last = amountHistory.GetLatestBefore(DateTime.Now);
            if (last is not null) yesterdayYi = last.Total;
        }

        // 快照入库（当日滚动）
        try
        {
            snapshots.AddRange(merged);
            snapshots.PruneKeepDate(DateTime.Today);
        }
        catch
        {
            // 快照属辅助数据，失败不影响刷新主流程
        }

        // 预警评估
        var triggered = EvaluateAlerts(merged);

        return new RefreshResult
        {
            Quotes = merged,
            TotalAmountYi = totalYi,
            YesterdayTotalYi = yesterdayYi,
            AmountDiff = totalYi is not null && yesterdayYi is not null ? totalYi - yesterdayYi : null,
            RefreshedAt = DateTime.Now,
            TriggeredAlerts = triggered,
        };
    }

    private List<AlertTrigger> EvaluateAlerts(IReadOnlyList<QuoteData> quotes)
    {
        var cfg = settings.Current;
        if (!cfg.AlertsEnabled) return [];

        List<AlertRuleEntity> rules;
        try
        {
            rules = alerts.GetEnabled();
        }
        catch
        {
            return [];
        }

        if (rules.Count == 0) return [];
        var today = DateTime.Today;
        var triggered = new List<AlertTrigger>();

        foreach (var rule in rules)
        {
            if (rule.LastTriggeredAt is not null && rule.LastTriggeredAt.Value.Date == today)
                continue; // 同一交易日只触发一次

            var q = quotes.FirstOrDefault(x => string.Equals(x.Code, rule.Code, StringComparison.OrdinalIgnoreCase));
            if (q?.ChangePct is not decimal change) continue;

            var dirOk = rule.Direction switch
            {
                AlertDirection.RiseOnly => change >= rule.ThresholdPct,
                AlertDirection.FallOnly => change <= -rule.ThresholdPct,
                _ => Math.Abs(change) >= rule.ThresholdPct,
            };
            if (!dirOk) continue;

            try
            {
                alerts.MarkTriggered(rule.Id, DateTime.Now);
            }
            catch
            {
                // 标记失败则可能重复提示，但不阻断
            }

            triggered.Add(new AlertTrigger(rule.Code, string.IsNullOrWhiteSpace(q.Name) ? rule.StockName : q.Name, change, rule.ThresholdPct, rule.Direction));
        }

        return triggered;
    }

    /// <summary>每天一次：量能历史保留 2 年，快照只保留当日。</summary>
    private void MaintenancePrune()
    {
        lock (PruneLock)
        {
            var today = DateTime.Today;
            if (_lastAmountPruneDate == today) return;
            _lastAmountPruneDate = today;
            try
            {
                amountHistory.PruneOlderThan(today.AddYears(-2));
            }
            catch
            {
                // 清理失败不影响主流程
            }
        }
    }
}
