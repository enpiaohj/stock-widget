using StockWidget.Core.Models.Ai;
using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>DeepSeek 返回内容 → 结构化结果（容错解析）。</summary>
public class AiResponseParserTests
{
    private const string ValidJson = """
    {
      "overallState": "震荡偏弱",
      "summary": "近20个交易日整体处于震荡整理状态。",
      "trendAnalysis": "当前价格位于MA5下方。",
      "volumeAnalysis": "成交量较近期均量有所增加。",
      "resistanceLevels": ["3960～4015"],
      "supportLevels": ["3840～3870", "3741附近"],
      "riskObservations": ["关注是否能够重新站稳MA20", "关注4000点附近压力"],
      "disclaimer": "以上内容仅基于当前行情和历史数据生成，不构成投资建议。"
    }
    """;

    [Fact]
    public void ParsesValidJson_AllFields()
    {
        var r = AiResponseParser.Parse(ValidJson);
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
        Assert.Equal("当前价格位于MA5下方。", r.TrendAnalysis);
        Assert.Equal("成交量较近期均量有所增加。", r.VolumeAnalysis);
        Assert.Equal(2, r.SupportLevels.Count);
        Assert.Equal(2, r.RiskObservations.Count);
        Assert.Contains("不构成投资建议", r.Disclaimer);
    }

    [Fact]
    public void ParsesJsonWrappedInCodeFence()
    {
        var wrapped = "```json\n" + ValidJson + "\n```";
        var r = AiResponseParser.Parse(wrapped);
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
    }

    [Fact]
    public void ParsesJsonWithSurroundingText()
    {
        var r = AiResponseParser.Parse("好的，以下是分析结果：\n" + ValidJson + "\n希望有帮助。");
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
    }

    [Fact]
    public void MissingFields_FallBackToEmpty()
    {
        var r = AiResponseParser.Parse("""{"overallState": "偏强"}""");
        Assert.NotNull(r);
        Assert.Equal("偏强", r.OverallState);
        Assert.Equal("", r.Summary);
        Assert.Empty(r.ResistanceLevels);
        Assert.Empty(r.RiskObservations);
    }

    [Fact]
    public void NullFields_FallBackToEmpty()
    {
        var r = AiResponseParser.Parse("""
        {
          "overallState": null,
          "summary": null,
          "resistanceLevels": null,
          "riskObservations": null
        }
        """);
        Assert.NotNull(r);
        Assert.Equal("", r.OverallState);
        Assert.Empty(r.ResistanceLevels);
        Assert.Empty(r.RiskObservations);
    }

    [Fact]
    public void NonArrayLevels_Ignored()
    {
        var r = AiResponseParser.Parse("""{"overallState": "ok", "supportLevels": "3840"}""");
        Assert.NotNull(r);
        Assert.Empty(r.SupportLevels);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("完全不是 JSON 的回复")]
    [InlineData("{ broken json")]
    public void InvalidContent_ReturnsNull(string content)
    {
        Assert.Null(AiResponseParser.Parse(content));
    }

    [Fact]
    public void EmptyJsonObject_ReturnsEmptyResult()
    {
        // 空对象合法但无内容：返回字段全空的结果（UI 按空内容处理）
        var r = AiResponseParser.Parse("{}");
        Assert.NotNull(r);
        Assert.Equal("", r.OverallState);
    }

    // ---------- 大小写策略（约束二） ----------

    [Fact]
    public void PascalCaseKeys_MappedToCorrectFields()
    {
        const string json = """
        {
          "OverallState": "震荡偏弱",
          "Summary": "摘要",
          "TrendAnalysis": "趋势",
          "VolumeAnalysis": "量价",
          "ResistanceLevels": ["3960"],
          "SupportLevels": ["3840"],
          "RiskObservations": ["风险"],
          "Disclaimer": "免责"
        }
        """;
        var r = AiResponseParser.Parse(json);
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
        Assert.Equal("摘要", r.Summary);
        Assert.Equal("趋势", r.TrendAnalysis);
        Assert.Equal("量价", r.VolumeAnalysis);
        Assert.Single(r.ResistanceLevels);
        Assert.Single(r.SupportLevels);
        Assert.Single(r.RiskObservations);
        Assert.Equal("免责", r.Disclaimer);
    }

    [Fact]
    public void SnakeCaseKeys_MappedToCorrectFields()
    {
        const string json = """
        {
          "overall_state": "震荡偏弱",
          "trend_analysis": "趋势",
          "volume_analysis": "量价",
          "resistance_levels": ["3960"],
          "support_levels": ["3840"],
          "risk_observations": ["风险"]
        }
        """;
        var r = AiResponseParser.Parse(json);
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
        Assert.Equal("趋势", r.TrendAnalysis);
        Assert.Equal("量价", r.VolumeAnalysis);
        Assert.Single(r.ResistanceLevels);
        Assert.Single(r.SupportLevels);
        Assert.Single(r.RiskObservations);
    }

    [Fact]
    public void FieldOrderChange_StillParses()
    {
        const string json = """
        {
          "disclaimer": "免责",
          "riskObservations": ["风险"],
          "supportLevels": ["3840"],
          "resistanceLevels": ["3960"],
          "volumeAnalysis": "量价",
          "trendAnalysis": "趋势",
          "summary": "摘要",
          "overallState": "震荡偏弱"
        }
        """;
        var r = AiResponseParser.Parse(json);
        Assert.NotNull(r);
        Assert.Equal("震荡偏弱", r.OverallState);
        Assert.Equal("摘要", r.Summary);
        Assert.Equal("趋势", r.TrendAnalysis);
        Assert.Equal("量价", r.VolumeAnalysis);
    }

    [Fact]
    public void ParsedFields_StayInTheirOwnField()
    {
        // 每个字段内容独立，Summary 不会吞并其他结构字段
        var r = AiResponseParser.Parse(ValidJson);
        Assert.NotNull(r);
        Assert.Equal("近20个交易日整体处于震荡整理状态。", r.Summary);
        Assert.DoesNotContain("MA5", r.Summary);
        Assert.DoesNotContain("3960", r.Summary);
        Assert.Equal("当前价格位于MA5下方。", r.TrendAnalysis);
    }
}
