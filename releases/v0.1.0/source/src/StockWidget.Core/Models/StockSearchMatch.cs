namespace StockWidget.Core.Models;

/// <summary>名称/拼音搜索候选：Code 已归一化（sh600390），Market 为 沪/深/港股/美股。</summary>
public sealed record StockSearchMatch(string Code, string Name, string Market);