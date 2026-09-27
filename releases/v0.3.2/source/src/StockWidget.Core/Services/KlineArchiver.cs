using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

public interface IKlineArchiver
{
    /// <summary>满足条件（交易日、15:05 后）时将行情按 Code+Date upsert 进日K表（重复归档覆盖）；否则静默跳过。</summary>
    void TryArchive(IReadOnlyList<QuoteData> quotes, DateTime now);
}

/// <summary>收盘归档：刷新链路已在 15:05 后抓到全自选 OHLC，直接落库，零额外请求。</summary>
public sealed class KlineArchiver(
    IDailyKlineRepository klines,
    ITradingCalendar calendar) : IKlineArchiver
{
    private static readonly TimeSpan ArchiveAfter = new(15, 5, 0);

    public void TryArchive(IReadOnlyList<QuoteData> quotes, DateTime now)
    {
        try
        {
            // 交易日判定基于传入的 now（与归档日期同源，调用方传 DateTime.Now），
            // 而非 IsTodayTrading() 的系统日期，保证可测试。
            if (!calendar.IsTradingDay(now.Date)) return;
            if (now.TimeOfDay < ArchiveAfter) return;

            var today = now.ToString("yyyy-MM-dd");
            // 不预过滤已归档代码：二次归档（盘后数据修正）应按 Code+Date 覆盖最新值
            var missing = quotes
                .Where(q => q.Success && q.Price is not null)
                .Select(q => new DailyKlineEntity
                {
                    Code = q.Code,
                    Date = today,
                    Open = q.Open ?? q.Price ?? 0m,
                    High = q.High ?? q.Price ?? 0m,
                    Low = q.Low ?? q.Price ?? 0m,
                    Close = q.Price ?? 0m,
                    Volume = q.Volume ?? 0m,
                    Amount = q.Amount ?? 0m,
                })
                .ToList();
            if (missing.Count == 0) return;
            klines.UpsertRange(missing);
        }
        catch
        {
            // 归档失败静默，下个刷新 tick 重试
        }
    }
}
