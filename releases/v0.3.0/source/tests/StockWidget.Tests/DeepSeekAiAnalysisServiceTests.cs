using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>AI 分析服务：缓存优先、强制刷新、未配置拦截。</summary>
public class DeepSeekAiAnalysisServiceTests : DatabaseTestBase
{
    private sealed class FakeClient(string content) : IAiApiClient
    {
        public int CallCount { get; private set; }
        public Task<string> CompleteAsync(string baseUrl, string apiKey, string model,
            string systemPrompt, string userPrompt, int timeoutSeconds, CancellationToken ct = default)
        {
            CallCount++;
            if (content is null) throw new InvalidOperationException("no content configured");
            return Task.FromResult(content);
        }

        public void Dispose() { }
    }

    private static MarketAnalysisContext MakeContext(decimal lastClose = 3891.60m) => new()
    {
        Code = "sh000001",
        Name = "上证指数",
        AssetType = "指数",
        AnalyzedAt = DateTime.Now,
        Quote = new QuoteData { Code = "sh000001", Price = lastClose, ChangePct = 0.71m, Success = true },
        Klines =
        [
            new DailyKlineEntity { Code = "sh000001", Date = "2026-09-15", Close = 3864.28m },
            new DailyKlineEntity { Code = "sh000001", Date = "2026-09-16", Close = lastClose },
        ],
        Ma5 = 10.54m, Ma10 = 10.49m, Ma20 = 10.44m,
        Change5Pct = 2.15m, Change20Pct = 5.32m, Change60Pct = 9.01m,
        RangeHigh = 11.10m, RangeLow = 9.90m,
    };

    private const string ValidContent = """
    {
      "overallState": "震荡偏弱",
      "summary": "摘要",
      "trendAnalysis": "趋势",
      "volumeAnalysis": "量价",
      "resistanceLevels": ["3960"],
      "supportLevels": ["3840"],
      "riskObservations": ["风险1"],
      "disclaimer": "不构成投资建议"
    }
    """;

    private DeepSeekAiAnalysisService MakeService(FakeClient client)
    {
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.Ai = new AiSettings { Enabled = true, BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", TimeoutSeconds = 60 };
        cfg.Ai.ApiKeyEncrypted = AiCredentialProtector.Protect("sk-test");
        settings.Save(cfg);

        return new DeepSeekAiAnalysisService(
            client,
            Provider.GetRequiredService<IAiAnalysisCacheRepository>(),
            settings);
    }

    [Fact]
    public async Task Miss_CallsApi_ParsesAndCaches()
    {
        var client = new FakeClient(ValidContent);
        var svc = MakeService(client);
        var cache = Provider.GetRequiredService<IAiAnalysisCacheRepository>();

        var result = await svc.AnalyzeAsync(MakeContext());

        Assert.Equal("震荡偏弱", result.OverallState);
        Assert.Equal(1, client.CallCount);
        // 缓存 Key 含 LastVolume（盘中 Close/Volume 持续变化时避免错误复用旧分析）
        Assert.NotNull(cache.GetResultJson("sh000001|2026-09-16|3891.60|0|deepseek-chat"));
    }

    [Fact]
    public async Task Hit_ReturnsCached_WithoutApiCall()
    {
        var client = new FakeClient(ValidContent);
        var svc = MakeService(client);
        var cache = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        cache.Upsert("sh000001|2026-09-16|3891.60|0|deepseek-chat",
            """{"overallState":"缓存值","summary":"s"}""");

        var result = await svc.AnalyzeAsync(MakeContext());

        Assert.Equal("缓存值", result.OverallState);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task ForceRefresh_IgnoresCache_AndOverwrites()
    {
        var client = new FakeClient(ValidContent);
        var svc = MakeService(client);
        var cache = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        cache.Upsert("sh000001|2026-09-16|3891.60|0|deepseek-chat", """{"overallState":"旧值"}""");

        var result = await svc.AnalyzeAsync(MakeContext(), forceRefresh: true);

        Assert.Equal("震荡偏弱", result.OverallState);
        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public async Task LastCloseChange_CacheMiss_RequestsAgain()
    {
        var client = new FakeClient(ValidContent);
        var svc = MakeService(client);
        await svc.AnalyzeAsync(MakeContext(lastClose: 3891.60m));

        await svc.AnalyzeAsync(MakeContext(lastClose: 3900.00m));

        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task NotConfigured_ThrowsWithFriendlyMessage()
    {
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.Ai = new AiSettings { Enabled = true, ApiKeyEncrypted = "" }; // 无 Key
        settings.Save(cfg);

        var svc = new DeepSeekAiAnalysisService(new FakeClient(ValidContent),
            Provider.GetRequiredService<IAiAnalysisCacheRepository>(), settings);

        var ex = await Assert.ThrowsAsync<AiNotConfiguredException>(() => svc.AnalyzeAsync(MakeContext()));
        Assert.Contains("设置", ex.Message);
    }

    [Fact]
    public async Task Disabled_ThrowsNotConfigured()
    {
        var settings = Provider.GetRequiredService<ISettingsService>();
        var cfg = settings.Current.Clone();
        cfg.Ai = new AiSettings { Enabled = false, ApiKeyEncrypted = AiCredentialProtector.Protect("sk") };
        settings.Save(cfg);

        var svc = new DeepSeekAiAnalysisService(new FakeClient(ValidContent),
            Provider.GetRequiredService<IAiAnalysisCacheRepository>(), settings);

        await Assert.ThrowsAsync<AiNotConfiguredException>(() => svc.AnalyzeAsync(MakeContext()));
    }

    [Fact]
    public async Task InvalidModelJson_PropagatesParseError()
    {
        var client = new FakeClient("这不是 JSON");
        var svc = MakeService(client);

        var ex = await Assert.ThrowsAsync<AiApiException>(() => svc.AnalyzeAsync(MakeContext()));
        Assert.Contains("JSON", ex.Message);
    }
}
