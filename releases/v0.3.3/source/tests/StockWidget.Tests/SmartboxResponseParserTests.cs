using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class SmartboxResponseParserTests
{
    // 腾讯 smartbox 响应（GBK 已解码）。候选段以 ; 连接，段内字段以 ~ 连接：
    // v_hint="..."~"股票代码"~"名称"~"拼音"~"市场"~"分类";
    [Fact]
    public void Parse_ChineseKeyword_ReturnsMatches()
    {
        const string resp =
            "v_hint=\"07ac08dd~3:sh000001~上证指数~ZS~4~\"~\"" +
            "sh000001\"~\"上证指数\"~\"szzs\"~\"sh\"~\"index\";" +
            "v_hint=\"\"~\"sz399001\"~\"深证成指\"~\"szcz\"~\"sz\"~\"index\";";
        var ms = SmartboxResponseParser.Parse(resp);
        Assert.NotEmpty(ms);
        Assert.Contains(ms, m => m.Code == "sh000001" && m.Name == "上证指数");
        Assert.Contains(ms, m => m.Code == "sz399001" && m.Name == "深证成指");
    }

    [Fact]
    public void Parse_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(SmartboxResponseParser.Parse(""));
        Assert.Empty(SmartboxResponseParser.Parse("garbage no tilde"));
        Assert.Empty(SmartboxResponseParser.Parse(null));
    }
}