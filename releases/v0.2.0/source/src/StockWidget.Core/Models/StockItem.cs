namespace StockWidget.Core.Models;

/// <summary>自选股条目（领域模型）。</summary>
public sealed record StockItem
{
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public int SortOrder { get; init; }
    public string Group { get; init; } = "";
    public bool IsPinned { get; init; }

    /// <summary>按代码前缀判断分类（指数 / ETF / 个股 / 港美股）。</summary>
    public StockCategory Category => StockClassifier.Classify(Code);
}

/// <summary>股票代码分类工具（与旧版代码段规则一致）。</summary>
public static class StockClassifier
{
    public static StockCategory Classify(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return StockCategory.Stock;

        if (code.StartsWith("hk", StringComparison.OrdinalIgnoreCase)) return StockCategory.HongKong;
        if (code.StartsWith("us", StringComparison.OrdinalIgnoreCase)) return StockCategory.UsStock;

        var market = code[..2].ToLowerInvariant();
        var num = code[2..];

        // 指数：sh000xxx / sz399xxx（含 000300 等中证指数）
        if (market == "sh" && num.StartsWith("000")) return StockCategory.Index;
        if (market == "sz" && num.StartsWith("399")) return StockCategory.Index;

        // ETF / LOF：沪 5 开头（510/511/512/513/515/518/560/562/588 等）、深 15/16 开头
        if (market == "sh" && num.Length == 6 && num[0] == '5') return StockCategory.Etf;
        if (market == "sz" && num.Length == 6 && (num[0] == '1' && (num[1] == '5' || num[1] == '6'))) return StockCategory.Etf;

        return StockCategory.Stock;
    }
}
