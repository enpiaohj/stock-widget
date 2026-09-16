using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

/// <summary>日K历史缺口回补服务：跳过最新、只插缺失、失败静默。</summary>
public class KlineBackfillServiceTests : DatabaseTestBase
{
    private static DailyKlineEntity K(string date, decimal close) =>
        new() { Code = "sh600390", Date = date, Open = close, High = close, Low = close, Close = close, Volume = 100, Amount = 50 };

    private sealed class FakeApi(List<DailyKlineEntity>? history = null, Exception? throwOnCall = null) : ITencentQuoteApi
    {
        public int CallCount { get; private set; }
        public Task<List<QuoteData>> FetchQuotesAsync(IReadOnlyList<string> codes, CancellationToken ct = default) => Task.FromResult(new List<QuoteData>());
        public Task<MinuteLineData?> FetchMinuteLineAsync(string code, CancellationToken ct = default) => Task.FromResult<MinuteLineData?>(null);
        public Task<IReadOnlyList<StockSearchMatch>> SearchSuggestAsync(string keyword, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StockSearchMatch>>([]);

        public Task<List<DailyKlineEntity>> FetchDailyKlineHistoryAsync(string code, int count, CancellationToken ct = default)
        {
            CallCount++;
            if (throwOnCall is not null) throw throwOnCall;
            return Task.FromResult(history ?? []);
        }
    }

    private KlineBackfillService MakeService(FakeApi api) => new(
        api,
        Provider.GetRequiredService<IDailyKlineRepository>(),
        new TradingCalendarService(TradingCalendarSeeder.GetBuiltIn()),
        requestDelayMs: 0);

    // 2026-09-16 是周三（空日历兜底为交易日）；上一交易日 = 09-15 周二
    private static readonly DateTime Wednesday = new(2026, 9, 16, 12, 0, 0);

    [Fact]
    public async Task FullHistoryNearLimit_AtPrevTradingDay_SkipsRequest()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        // 历史根数接近上限（318 根）且最新 = 上一交易日 → 稳态无缺口
        var full = new List<DailyKlineEntity>();
        for (var i = 0; i < 318; i++)
            full.Add(K(DateTime.Today.AddDays(-1 - i).ToString("yyyy-MM-dd"), 10m));
        repo.UpsertRange(full);
        var api = new FakeApi(history: []);

        var inserted = await MakeService(api).BackfillAsync(["sh600390"], Wednesday);

        Assert.Equal(0, inserted);
        Assert.Equal(0, api.CallCount); // 未发请求
    }

    [Fact]
    public async Task TodayArchiveOnly_StillBackfills()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        // 只有今日归档记录（15:05 归档先于回补的时序）——历史缺口仍需回补
        repo.UpsertRange([K("2026-09-16", 10m)]);
        var api = new FakeApi(history: [K("2026-09-15", 10m), K("2026-09-16", 77m)]);

        var inserted = await MakeService(api).BackfillAsync(["sh600390"], Wednesday);

        Assert.Equal(1, inserted); // 09-15 补上，今日不插
        Assert.Equal(1, api.CallCount);
    }

    [Fact]
    public async Task MissingDays_InsertedPastOnly_AndExistingNotOverwritten()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("2026-09-14", 99m)]); // 已有 09-14（原值保护）
        // 返回 09-11/09-14/09-15 三天 + 今日（09-16，应由 15:05 归档负责）
        var api = new FakeApi(history: [K("2026-09-11", 9m), K("2026-09-14", 98m), K("2026-09-15", 10m), K("2026-09-16", 77m)]);

        var inserted = await MakeService(api).BackfillAsync(["sh600390"], Wednesday);

        Assert.Equal(2, inserted); // 09-11、09-15
        var rows = repo.GetByCode("sh600390", 10);
        Assert.Equal(99m, rows.Single(r => r.Date == "2026-09-14").Close); // 不覆盖（保留本地原值 99，接口值 98 被忽略）
        Assert.Equal(10m, rows.Single(r => r.Date == "2026-09-15").Close);
        Assert.DoesNotContain(rows, r => r.Date == "2026-09-16"); // 今日不回补
    }

    [Fact]
    public async Task ApiFailure_IsSilent_AndContinues()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        var api = new FakeApi(throwOnCall: new InvalidOperationException("network"));

        var inserted = await MakeService(api).BackfillAsync(["sh600390"], Wednesday);

        Assert.Equal(0, inserted);
        Assert.Empty(repo.GetByCode("sh600390", 10));
    }

    [Fact]
    public async Task NoRecordAtAll_Backfills()
    {
        var api = new FakeApi(history: [K("2026-09-15", 10m)]);
        var inserted = await MakeService(api).BackfillAsync(["sh600390"], Wednesday);
        Assert.Equal(1, inserted);
        Assert.Equal(1, api.CallCount);
    }
}
