using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

public interface IKlineBackfillService
{
    /// <summary>
    /// 串行回补每只代码缺失的历史日K（腾讯 fqkline，前复权 320 根）。
    /// 只插本地缺失记录、不覆盖已有（避免与 15:05 实时归档的复权口径冲突）；
    /// 今日数据一律留给收盘归档链路。单只失败静默跳过。返回插入总条数。
    /// progress 上报 (已完成数, 总数)，每只代码处理完（含跳过）后触发。
    /// </summary>
    Task<int> BackfillAsync(IReadOnlyList<string> codes, DateTime today, CancellationToken ct = default,
        IProgress<(int Done, int Total)>? progress = null);
}

/// <summary>启动后后台执行的日K缺口回补：稳态（最新记录已到上一交易日）不发请求。</summary>
public sealed class KlineBackfillService(
    ITencentQuoteApi api,
    IDailyKlineRepository klines,
    ITradingCalendar calendar,
    int requestDelayMs = 300) : IKlineBackfillService
{
    private const int HistoryCount = 320;

    public async Task<int> BackfillAsync(IReadOnlyList<string> codes, DateTime today, CancellationToken ct = default,
        IProgress<(int Done, int Total)>? progress = null)
    {
        var prevTradingDay = PrevTradingDay(today);
        var cutoff = today.ToString("yyyy-MM-dd");
        var total = 0;
        var done = 0;

        foreach (var code in codes)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                // 稳态无缺口判定：本地根数接近请求上限 且 最新记录不落后于上一交易日
                // （不能只看最新日期——15:05 归档的今日记录会让 latest 恒 >= 上一交易日，误跳过全部回补）
                var recent = klines.GetByCode(code, HistoryCount);
                var latestDate = recent.Count > 0 ? recent[^1].Date : null;
                if (latestDate is not null
                    && string.CompareOrdinal(latestDate, prevTradingDay) >= 0
                    && recent.Count >= HistoryCount - 2)
                    continue;

                var history = await api.FetchDailyKlineHistoryAsync(code, HistoryCount, ct).ConfigureAwait(false);
                total += klines.InsertMissing(history.Where(k => string.CompareOrdinal(k.Date, cutoff) < 0));
            }
            catch
            {
                // 单只失败静默：不影响其余，下次启动重试
            }

            done++;
            progress?.Report((done, codes.Count));

            if (requestDelayMs > 0)
                await Task.Delay(requestDelayMs, CancellationToken.None).ConfigureAwait(false);
        }
        return total;
    }

    /// <summary>严格早于 today 的最近交易日（周末兜底规则与日历一致）。</summary>
    private string PrevTradingDay(DateTime today)
    {
        var day = today.Date.AddDays(-1);
        while (!calendar.IsTradingDay(day))
            day = day.AddDays(-1);
        return day.ToString("yyyy-MM-dd");
    }
}
