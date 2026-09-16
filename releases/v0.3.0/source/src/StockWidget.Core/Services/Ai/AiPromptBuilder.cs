using System.Globalization;
using System.Text.Json;
using StockWidget.Core.Models.Ai;

namespace StockWidget.Core.Services.Ai;

/// <summary>AI 分析 Prompt 构造：System Prompt（角色与红线）+ 结构化行情 JSON。</summary>
public static class AiPromptBuilder
{
    public const string SystemPrompt = """
    你是一名金融市场数据分析助手。

    你的任务是根据程序提供的真实行情数据和历史K线数据，
    对当前资产的趋势结构、量价关系、关键位置和风险观察进行客观解释。

    必须遵守以下规则：

    1. 只能基于提供的数据进行分析。
    2. 不得编造新闻、公告、政策、资金流向或基本面信息。
    3. 不得假设程序未提供的数据。
    4. 不得声称知道未来行情。
    5. 不得保证上涨或下跌。
    6. 不输出“强烈买入”“立即卖出”“必涨”“必跌”等明确交易指令。
    7. 可以分析趋势、支撑、压力、量价关系和风险。
    8. 如果数据不足，必须明确说明数据不足。
    9. 使用简洁中文。
    10. 不输出 Markdown。
    11. 返回严格 JSON。
    12. JSON 字段必须严格符合调用方指定结构。
    13. 分析仅基于数据本身，不构成投资建议。

    返回 JSON 必须包含以下全部字段，且键名严格一致（camelCase）：

    {
      "overallState": "字符串：1～8 个字的综合判断（如“震荡偏弱”“放量上攻”）",
      "summary": "字符串：仅 1～2 句综合摘要，不得包含趋势/量价/关键位置的详细内容",
      "trendAnalysis": "字符串：只输出趋势结构分析（均线位置、方向、结构）",
      "volumeAnalysis": "字符串：只输出量价关系分析（结合提供的均量与变化百分比）",
      "resistanceLevels": ["字符串数组：只列压力位或压力区间"],
      "supportLevels": ["字符串数组：只列支撑位或支撑区间"],
      "riskObservations": ["字符串数组：只列风险观察要点"],
      "disclaimer": "字符串：免责声明"
    }

    严格要求：
    - 每个字段都必须存在，不得省略。
    - 不允许把趋势、量价、关键位置的内容塞入 summary。
    - resistanceLevels / supportLevels / riskObservations 必须是字符串数组。
    - 不允许返回 Markdown 或 JSON 以外的任何文字。
    """;

    public static string BuildUserPrompt(MarketAnalysisContext ctx)
    {
        var list = ctx.Klines.TakeLast(120).ToList();
        var klines = list.Select(k => new
        {
            date = k.Date,
            open = k.Open,
            high = k.High,
            low = k.Low,
            close = k.Close,
            volume = k.Volume,
        });

        // 量能统计（本地明确计算，量价解释不依赖模型自行推算）
        var lastVol = list.Count > 0 ? list[^1].Volume : 0m;
        var vol5 = list.Count > 0 ? list.TakeLast(5).Average(k => k.Volume) : 0m;
        var vol20 = list.Count >= 20 ? list.TakeLast(20).Average(k => k.Volume) : vol5;

        var payload = new
        {
            code = ctx.Code,
            name = ctx.Name,
            assetType = ctx.AssetType,
            analyzedAt = ctx.AnalyzedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            quote = new
            {
                price = ctx.Quote.Price,
                changePct = ctx.Quote.ChangePct,
                open = ctx.Quote.Open,
                high = ctx.Quote.High,
                low = ctx.Quote.Low,
                prevClose = ctx.Quote.PrevClose,
                volume = ctx.Quote.Volume,
                amount = ctx.Quote.Amount,
                turnover = ctx.Quote.Turnover,
                amplitude = ctx.Quote.Amplitude,
                marketCap = ctx.Quote.MarketCap,
            },
            ma5 = ctx.Ma5,
            ma10 = ctx.Ma10,
            ma20 = ctx.Ma20,
            change5Pct = ctx.Change5Pct,
            change20Pct = ctx.Change20Pct,
            change60Pct = ctx.Change60Pct,
            rangeHigh = ctx.RangeHigh,
            rangeLow = ctx.RangeLow,
            volume5Avg = Math.Round(vol5, 2),
            volume20Avg = Math.Round(vol20, 2),
            volumeVs5AvgPct = PctChange(lastVol, vol5),
            volumeVs20AvgPct = PctChange(lastVol, vol20),
            klines,
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private static decimal PctChange(decimal current, decimal baseline) =>
        baseline == 0 ? 0 : Math.Round((current - baseline) / baseline * 100, 2);
}
