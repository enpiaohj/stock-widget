using System.Text.Json;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>行情上下文 → User Prompt（结构化 JSON）。</summary>
public class AiPromptBuilderTests
{
    private static MarketAnalysisContext MakeContext(int klineCount = 120)
    {
        var bars = new List<DailyKlineEntity>();
        var start = new DateTime(2026, 1, 5);
        for (var i = 0; i < klineCount; i++)
        {
            var close = 10m + i * 0.01m;
            bars.Add(new DailyKlineEntity
            {
                Code = "sh000001",
                Date = start.AddDays(i).ToString("yyyy-MM-dd"),
                Open = close - 0.05m,
                High = close + 0.10m,
                Low = close - 0.10m,
                Close = close,
                Volume = 10000 + i * 100,
                Amount = 500 + i,
            });
        }

        return new MarketAnalysisContext
        {
            Code = "sh000001",
            Name = "上证指数",
            AssetType = "指数",
            AnalyzedAt = new DateTime(2026, 9, 16, 20, 42, 0),
            Quote = new QuoteData
            {
                Code = "sh000001", Name = "上证指数", Price = 3891.60m, ChangePct = 0.71m,
                Open = 3861.75m, High = 3894.66m, Low = 3842.72m, PrevClose = 3864.28m,
                Volume = 5506941, Amount = 62000, Turnover = 0.85m, Amplitude = 1.34m,
                MarketCap = 609027, Success = true,
            },
            Klines = bars,
            Ma5 = 10.54m, Ma10 = 10.49m, Ma20 = 10.44m,
            Change5Pct = 2.15m, Change20Pct = 5.32m, Change60Pct = 9.01m,
            RangeHigh = 11.10m, RangeLow = 9.90m,
        };
    }

    [Fact]
    public void SystemPrompt_ContainsCoreRules()
    {
        Assert.Contains("只能基于提供的数据进行分析", AiPromptBuilder.SystemPrompt);
        Assert.Contains("不得编造新闻", AiPromptBuilder.SystemPrompt);
        Assert.Contains("不输出", AiPromptBuilder.SystemPrompt);
        Assert.Contains("返回严格 JSON", AiPromptBuilder.SystemPrompt);
        Assert.Contains("不构成投资建议", AiPromptBuilder.SystemPrompt);
    }

    [Fact]
    public void BuildUserPrompt_ContainsBasics_Trend_AndKlines()
    {
        var json = AiPromptBuilder.BuildUserPrompt(MakeContext());
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 基础信息
        Assert.Equal("sh000001", root.GetProperty("code").GetString());
        Assert.Equal("上证指数", root.GetProperty("name").GetString());
        Assert.Equal("指数", root.GetProperty("assetType").GetString());
        Assert.Equal(3891.60m, root.GetProperty("quote").GetProperty("price").GetDecimal());
        Assert.Equal(0.71m, root.GetProperty("quote").GetProperty("changePct").GetDecimal());

        // 趋势数据
        Assert.Equal(10.54m, root.GetProperty("ma5").GetDecimal());
        Assert.Equal(10.49m, root.GetProperty("ma10").GetDecimal());
        Assert.Equal(10.44m, root.GetProperty("ma20").GetDecimal());
        Assert.Equal(2.15m, root.GetProperty("change5Pct").GetDecimal());
        Assert.Equal(5.32m, root.GetProperty("change20Pct").GetDecimal());
        Assert.Equal(9.01m, root.GetProperty("change60Pct").GetDecimal());
        Assert.Equal(11.10m, root.GetProperty("rangeHigh").GetDecimal());
        Assert.Equal(9.90m, root.GetProperty("rangeLow").GetDecimal());

        // 日K：120 根 + 字段完整
        var klines = root.GetProperty("klines");
        Assert.Equal(120, klines.GetArrayLength());
        var first = klines[0];
        Assert.Equal("2026-01-05", first.GetProperty("date").GetString());
        Assert.Equal(9.95m, first.GetProperty("open").GetDecimal());
        Assert.Equal(10.00m, first.GetProperty("close").GetDecimal());
        Assert.Equal(10000, first.GetProperty("volume").GetDecimal());

        // 量能统计（约束：量价关系基于本地明确计算）
        // 120 根 = i=0..119；volume = 10000 + i*100 → 末根(i=119) 21900
        // 5日均量 = mean(i=115..119) = mean(21500..21900) = 21700
        // 20日均量 = mean(i=100..119) = mean(20000..21900) = 20950
        // 末根 21900 vs 5日均量 21700 → +0.92%；vs 20日均量 20950 → +4.53%
        Assert.Equal(21700m, root.GetProperty("volume5Avg").GetDecimal());
        Assert.Equal(20950m, root.GetProperty("volume20Avg").GetDecimal());
        Assert.Equal(0.92m, root.GetProperty("volumeVs5AvgPct").GetDecimal());
        Assert.Equal(4.53m, root.GetProperty("volumeVs20AvgPct").GetDecimal());
    }

    [Fact]
    public void BuildUserPrompt_TruncatesToMostRecent120()
    {
        var json = AiPromptBuilder.BuildUserPrompt(MakeContext(klineCount: 320));
        using var doc = JsonDocument.Parse(json);
        var klines = doc.RootElement.GetProperty("klines");
        Assert.Equal(120, klines.GetArrayLength());
        // 合成数据起于 2026-01-05（i=0 连续自然日），最近 120 根 = i=200..319
        Assert.Equal("2026-07-24", klines[0].GetProperty("date").GetString());
        Assert.Equal("2026-11-20", klines[119].GetProperty("date").GetString());
    }
}
