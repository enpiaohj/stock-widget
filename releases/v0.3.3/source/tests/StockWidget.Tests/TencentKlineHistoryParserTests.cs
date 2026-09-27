using System.Text.Json;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

/// <summary>腾讯历史日K（fqkline）JSON 解析。</summary>
public class TencentKlineHistoryParserTests
{
    private static List<StockWidget.Core.Data.Entities.DailyKlineEntity> Parse(string json, string code = "sh600390")
    {
        using var doc = JsonDocument.Parse(json);
        return TencentKlineHistoryParser.Parse(doc.RootElement, code);
    }

    private const string QfqJson = """
    {
      "code": 0,
      "data": {
        "sh600390": {
          "qfqday": [
            ["2026-09-14", "10.00", "10.50", "10.60", "9.90", "12345.00"],
            ["2026-09-15", "10.50", "10.80", "10.90", "10.40", "23456.00"]
          ],
          "qt": {}
        }
      }
    }
    """;

    [Fact]
    public void ParsesQfqdayRows_WithOpenCloseHighLowVolumeOrder()
    {
        var rows = Parse(QfqJson);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("sh600390", r.Code));

        var first = rows[0];
        Assert.Equal("2026-09-14", first.Date);
        Assert.Equal(10.00m, first.Open);
        Assert.Equal(10.60m, first.High);
        Assert.Equal(9.90m, first.Low);
        Assert.Equal(10.50m, first.Close); // 第 3 列是收盘价（腾讯口径 date/open/close/high/low）
        Assert.Equal(12345m, first.Volume);
    }

    [Fact]
    public void FallsBackToDayKey_WhenQfqdayMissing()
    {
        const string json = """
        {
          "code": 0,
          "data": {
            "sh600390": {
              "day": [["2026-09-15", "10.50", "10.80", "10.90", "10.40", "23456.00"]]
            }
          }
        }
        """;
        var rows = Parse(json);
        Assert.Single(rows);
        Assert.Equal("2026-09-15", rows[0].Date);
    }

    [Fact]
    public void IgnoresTrailingObjectEntries_AndBadRows()
    {
        const string json = """
        {
          "code": 0,
          "data": {
            "sh600390": {
              "qfqday": [
                ["2026-09-14", "10.00", "10.50", "10.60", "9.90", "12345.00"],
                ["bad-row"],
                { "amount": "1" },
                ["2026-09-15", "10.50", "10.80", "10.90", "10.40", "23456.00", "额外列被忽略"]
              ]
            }
          }
        }
        """;
        var rows = Parse(json);
        Assert.Equal(2, rows.Count);
        Assert.Equal("2026-09-14", rows[0].Date);
        Assert.Equal("2026-09-15", rows[1].Date);
    }

    [Fact]
    public void UnknownCode_OrErrorResponse_ReturnsEmpty()
    {
        const string json = """{ "code": 0, "data": { "other": {} } }""";
        Assert.Empty(Parse(json));

        const string err = """{ "code": -1 }""";
        Assert.Empty(Parse(err));
    }
}
