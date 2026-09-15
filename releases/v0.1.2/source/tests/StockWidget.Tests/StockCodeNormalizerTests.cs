using StockWidget.Core.Models;
using Xunit;

namespace StockWidget.Tests;

/// <summary>auto_fix_code 归一化规则移植验证（规则与顺序与旧版一致）。</summary>
public class StockCodeNormalizerTests
{
    [Theory]
    [InlineData("sh600390", "sh600390")]
    [InlineData("SZ399001", "sz399001")]
    [InlineData("hk00700", "hk00700")]
    [InlineData("usAAPL", "usaapl")] // 已带前缀：仅小写化（与旧版一致）
    public void Normalize_WithPrefix_ReturnsLowercased(string input, string expected)
    {
        Assert.Equal(expected, StockCodeNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("600390", "sh600390")] // 沪市主板
    [InlineData("688981", "sh688981")] // 科创板
    [InlineData("510050", "sh510050")] // 沪 ETF
    [InlineData("512480", "sh512480")] // 半导体 ETF
    [InlineData("588000", "sh588000")] // 科创 50 ETF
    [InlineData("110038", "sh110038")] // 沪可转债
    public void Normalize_ShanghaiPrefixes(string input, string expected)
    {
        Assert.Equal(expected, StockCodeNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("000001", "sz000001")] // 平安银行（旧版规则：深市前缀优先于指数补充集）
    [InlineData("002797", "sz002797")] // 第一创业
    [InlineData("300750", "sz300750")] // 宁德时代
    [InlineData("399006", "sz399006")] // 创业板指
    [InlineData("159819", "sz159819")] // 深 ETF
    [InlineData("12xxxx", "sz123456")] // 前缀 12 命中（取数字 123456）
    public void Normalize_ShenzhenPrefixes(string input, string expected)
    {
        var result = StockCodeNormalizer.Normalize(input == "12xxxx" ? "123456" : input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("00700", "hk00700")]  // 5 位数字补零
    [InlineData("700", "sh700")]      // 3 位数字走前缀规则（700 新股前缀，与旧版一致）
    [InlineData("9988", "hk09988")]   // 阿里巴巴
    public void Normalize_HkAndEdge(string input, string expected)
    {
        Assert.Equal(expected, StockCodeNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("AAPL", "usAAPL")]
    [InlineData("TSLA", "usTSLA")]
    public void Normalize_UsLetters(string input, string expected)
    {
        Assert.Equal(expected, StockCodeNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    [InlineData("abcdefg", "abcdefg")] // >5 位字母，不满足美股规则，原样返回
    public void Normalize_InvalidInput(string? input, string expected)
    {
        Assert.Equal(expected, StockCodeNormalizer.Normalize(input));
    }

    [Fact]
    public void StripPrefix_RemovesMarket()
    {
        Assert.Equal("600390", StockCodeNormalizer.StripPrefix("sh600390"));
        Assert.Equal("399001", StockCodeNormalizer.StripPrefix("sz399001"));
        Assert.Equal("AAPL", StockCodeNormalizer.StripPrefix("usAAPL"));
    }

    [Fact]
    public void ParseDecimal_HandlesInvalid()
    {
        Assert.Null(StockCodeNormalizer.ParseDecimal(null));
        Assert.Null(StockCodeNormalizer.ParseDecimal(""));
        Assert.Null(StockCodeNormalizer.ParseDecimal("失败"));
        Assert.Equal(10.74m, StockCodeNormalizer.ParseDecimal("10.74"));
        Assert.Equal(206495m, StockCodeNormalizer.ParseDecimal("206495"));
    }
}
