using System.Text.Json;
using StockWidget.Core.Services;
using Xunit;

namespace StockWidget.Tests;

/// <summary>腾讯响应解析验证（字段下标与旧版一致）。</summary>
public class TencentResponseParserTests
{
    /// <summary>按下标精确构造一行响应：v_{code}="…~…";（数组长度 50 覆盖用到的最大下标 44）。</summary>
    private static string BuildLine(string code, string name, params (int Index, string Value)[] fields)
    {
        var parts = new string[50];
        parts[0] = "1";
        parts[1] = name;
        parts[2] = code;
        foreach (var (index, value) in fields) parts[index] = value;
        return $"v_{code}=\"{string.Join("~", parts)}\";";
    }

    [Fact]
    public void ParseBatch_SingleLine_ParsesAllFields()
    {
        // 下标与旧版映射一致：4昨收 5开盘 6成交量 32涨跌幅 33最高 34最低 37成交额 38换手 43振幅 44市值
        var text = BuildLine("sh600000", "浦发银行",
            (3, "10.74"), (4, "10.72"), (5, "10.72"), (6, "206495"),
            (32, "-0.19"), (33, "10.85"), (34, "10.66"), (37, "2221933.00"),
            (38, "0.34"), (43, "1.78"), (44, "3551.00"));

        var quotes = TencentResponseParser.ParseBatch(text, ["sh600000"]);

        var q = Assert.Single(quotes);
        Assert.True(q.Success);
        Assert.Equal("sh600000", q.Code);
        Assert.Equal("浦发银行", q.Name);
        Assert.Equal(10.74m, q.Price);
        Assert.Equal(-0.19m, q.ChangePct);
        Assert.Equal(206495m, q.Volume);
        Assert.Equal(0.34m, q.Turnover);
        Assert.Equal(10.85m, q.High);
        Assert.Equal(10.66m, q.Low);
        Assert.Equal(2221933.00m, q.Amount);
        Assert.Equal(10.72m, q.Open);
        Assert.Equal(10.72m, q.PrevClose);
        Assert.Equal(3551.00m, q.MarketCap);
        Assert.Equal(1.78m, q.Amplitude);
    }

    [Fact]
    public void ParseBatch_MultipleLines_ParsesAll()
    {
        var text = BuildLine("sh600000", "浦发银行", (3, "10.74"))
                 + BuildLine("sz399001", "深证成指", (3, "13000.00"));

        var quotes = TencentResponseParser.ParseBatch(text, ["sh600000", "sz399001"]);

        Assert.Equal(2, quotes.Count);
        Assert.Equal("浦发银行", quotes[0].Name);
        Assert.Equal("深证成指", quotes[1].Name);
        Assert.Equal(13000.00m, quotes[1].Price);
    }

    [Fact]
    public void ParseBatch_MissingPrice_MarksFailed()
    {
        var text = BuildLine("sh600000", "", (3, ""));
        Assert.Empty(TencentResponseParser.ParseBatch(text, ["sh600000"]));
    }

    [Fact]
    public void ParseBatch_UnrequestedCode_Ignored()
    {
        var text = BuildLine("sz000001", "平安银行", (3, "12.00"));
        Assert.Empty(TencentResponseParser.ParseBatch(text, ["sh600000"]));
    }

    [Fact]
    public void ParseBatch_EmptyText_ReturnsEmpty()
    {
        Assert.Empty(TencentResponseParser.ParseBatch("", ["sh600000"]));
        Assert.Empty(TencentResponseParser.ParseBatch("v_sh600000=\"\";", ["sh600000"]));
    }

    [Fact]
    public void ParseMinute_ParsesPointsAndPrevClose()
    {
        var json = """
        {"code":0,"msg":"","data":{"sh600000":{"data":{"data":["0930 10.73 1123","0931 10.75 2234","0932 10.74 3345"],"date":"20251217"},
          "qt":{"v_sh600000":["1","浦发银行","600000","10.74","10.72","10.72","206495"]}}}}
        """;
        using var doc = JsonDocument.Parse(json);
        var minute = TencentResponseParser.ParseMinute(doc.RootElement, "sh600000");

        Assert.NotNull(minute);
        Assert.Equal(3, minute!.Points.Count);
        Assert.Equal("0930", minute.Points[0].Time);
        Assert.Equal(10.75m, minute.Points[1].Price);
        Assert.Equal(10.72m, minute.PrevClose);
        Assert.Equal("20251217", minute.Date);
    }

    [Fact]
    public void ParseMinute_ExtractsPerMinuteVolume()
    {
        // 行格式 "HHmm price 累计量(手)"：100/350/350 → 分钟量 100/250/0
        var json = """
        {"code":0,"msg":"","data":{"sh600390":{"data":{"data":["0930 10.50 100","0931 10.60 350","0932 10.40 350"],"date":"20260916"},
          "qt":{"v_sh600390":["1","中钨高新","600390","10.50","10.40","10.40","100"]}}}}
        """;
        using var doc = JsonDocument.Parse(json);
        var minute = TencentResponseParser.ParseMinute(doc.RootElement, "sh600390");

        Assert.NotNull(minute);
        Assert.Equal(3, minute!.Points.Count);
        Assert.Equal(100m, minute.Points[0].Volume); // 首分钟=累计
        Assert.Equal(250m, minute.Points[1].Volume); // 350-100
        Assert.Equal(0m, minute.Points[2].Volume);   // 350-350
    }

    [Fact]
    public void ParseMinute_BadCode_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{"code":1,"msg":"error","data":{}}""");
        Assert.Null(TencentResponseParser.ParseMinute(doc.RootElement, "sh600000"));
    }
}
