using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Services;
using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>AI 分析结果缓存（SQLite）。</summary>
public class AiAnalysisCacheRepositoryTests : DatabaseTestBase
{
    private const string Key = "sh000001|2026-09-16|3891.60|deepseek-chat";

    [Fact]
    public void UpsertThenGet_RoundTrips()
    {
        var repo = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        repo.Upsert(Key, """{"overallState":"震荡偏弱"}""");

        Assert.Equal("""{"overallState":"震荡偏弱"}""", repo.GetResultJson(Key));
    }

    [Fact]
    public void GetResultJson_MissingKey_ReturnsNull()
    {
        var repo = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        Assert.Null(repo.GetResultJson("no-such-key"));
    }

    [Fact]
    public void Upsert_SameKey_Overwrites()
    {
        var repo = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        repo.Upsert(Key, "old");
        repo.Upsert(Key, "new");

        Assert.Equal("new", repo.GetResultJson(Key));
    }

    [Fact]
    public void Upsert_EmptyKeyOrJson_Ignored()
    {
        var repo = Provider.GetRequiredService<IAiAnalysisCacheRepository>();
        repo.Upsert("", "x");
        repo.Upsert("key2", "");
        Assert.Null(repo.GetResultJson(""));
        Assert.Null(repo.GetResultJson("key2"));
    }
}
