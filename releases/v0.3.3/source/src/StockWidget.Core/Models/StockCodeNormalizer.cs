using System.Globalization;

namespace StockWidget.Core.Models;

/// <summary>
/// 股票代码归一化（完整移植旧版 auto_fix_code，规则顺序保持一致）：
/// 1. 已带 sh/sz/hk/us 前缀 → 原样返回（小写）
/// 2. 纯字母 ≤5 位 → 美股 us 前缀
/// 3. 数字 4–5 位 → 港股（补零到 5 位）
/// 4. 沪市前缀命中 → sh；深市前缀命中 → sz；指数补充集 → sh
/// 5. 兜底返回纯数字串或原文
/// </summary>
public static class StockCodeNormalizer
{
    private static readonly string[] ShPrefixes =
    [
        "600", "601", "603", "605", "688", "689", "6",
        "510", "511", "512", "513", "515", "518", "56", "58", "5",
        "500", "501",
        "110", "113", "120",
        "730", "700",
    ];

    private static readonly string[] SzPrefixes =
    [
        "000", "001",
        "002",
        "300",
        "399",
        "430", "831", "832", "833",
        "870", "871", "872",
        "15", "16", "17", "18",
        "12", "11",
    ];

    private static readonly HashSet<string> IndexExtras = new(StringComparer.Ordinal)
    {
        "000001", "000300", "000016", "000905",
    };

    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        var code = input.Trim().ToLowerInvariant();
        if (code.StartsWith("sh") || code.StartsWith("sz") || code.StartsWith("hk") || code.StartsWith("us"))
            return code;

        if (code.All(char.IsLetter) && code.Length <= 5)
            return $"us{code.ToUpperInvariant()}";

        var num = new string(code.Where(char.IsDigit).ToArray());
        if (num.Length is 4 or 5)
            return $"hk{num.PadLeft(5, '0')}";

        if (ShPrefixes.Any(p => num.StartsWith(p, StringComparison.Ordinal)))
            return $"sh{num}";
        if (SzPrefixes.Any(p => num.StartsWith(p, StringComparison.Ordinal)))
            return $"sz{num}";
        if (IndexExtras.Contains(num))
            return $"sh{num}";

        return num.Length > 0 ? num : code;
    }

    /// <summary>去掉市场前缀并大写（展示用，如 sh600390 → 600390）。</summary>
    public static string StripPrefix(string code)
    {
        if (code.Length > 2 && (code.StartsWith("sh", StringComparison.OrdinalIgnoreCase)
                                || code.StartsWith("sz", StringComparison.OrdinalIgnoreCase)
                                || code.StartsWith("hk", StringComparison.OrdinalIgnoreCase)
                                || code.StartsWith("us", StringComparison.OrdinalIgnoreCase)))
            return code[2..].ToUpperInvariant();
        return code.ToUpperInvariant();
    }

    /// <summary>安全解析 decimal（处理空串 / 非法值），失败返回 null。</summary>
    public static decimal? ParseDecimal(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return decimal.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
