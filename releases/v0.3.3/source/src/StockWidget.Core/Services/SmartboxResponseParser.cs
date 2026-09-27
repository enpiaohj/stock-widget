using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

/// <summary>
/// 腾讯 smartbox（smartbox.gtimg.cn/s3）响应解析（GBK 已解码）。
/// 候选段以 ; 连接，段内字段以 ~ 连接：v_hint="..."~"代码"~"名称"~"拼音"~"市场"~"分类";
/// 失败/异常静默返回空列表（UI 兜底）。
/// </summary>
public static class SmartboxResponseParser
{
    public static IReadOnlyList<StockSearchMatch> Parse(string? text)
    {
        var result = new List<StockSearchMatch>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = segment.Split('~');
            var code = ExtractCode(fields);
            if (code == null) continue;

            // 名称 = code 之后第一个非空字段；市场 = code 之后最接近的名称之后字段（映射）
            string? name = null;
            for (var i = 0; i < fields.Length; i++)
            {
                var f = fields[i].Trim().Trim('"');
                if (f.Length == 0) continue;
                if (string.Equals(f, code, StringComparison.OrdinalIgnoreCase))
                {
                    // code 在 fields 中；name 是它之后的第一个非空
                    for (var j = i + 1; j < fields.Length; j++)
                    {
                        var nf = fields[j].Trim().Trim('"');
                        if (nf.Length == 0) continue;
                        if (LooksLikeMarketCode(nf)) break;
                        name = nf;
                        break;
                    }
                    break;
                }
            }

            var normalized = StockCodeNormalizer.Normalize(code);
            if (normalized.Length == 0) continue;
            result.Add(new StockSearchMatch(normalized, name ?? "", MarketFrom(fields)));
        }
        return result;
    }

    private static string? ExtractCode(string[] fields)
    {
        foreach (var f in fields)
        {
            var t = f.Trim().Trim('"');
            if (LooksLikeCode(t)) return t;
        }
        return null;
    }

    private static bool LooksLikeCode(string s) =>
        s.Length >= 5 && (s.All(char.IsDigit)
                          || s.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                          || s.StartsWith("sz", StringComparison.OrdinalIgnoreCase)
                          || s.StartsWith("hk", StringComparison.OrdinalIgnoreCase)
                          || s.StartsWith("us", StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeMarketCode(string s) =>
        s is "1" or "2" or "3" or "4" or "sh" or "sz" or "hk" or "us";

    private static string MarketFrom(string[] fields)
    {
        // 市场常见字段：优先识别 "sh"/"sz"/"hk"/"us" 或分类代码；未知 → "A股"
        foreach (var f in fields)
        {
            var t = f.Trim().Trim('"');
            if (t is "sh" or "1" or "2" or "5") return "A股";
            if (t is "hk" or "3") return "港股";
            if (t is "us" or "4") return "美股";
        }
        return "A股";
    }
}