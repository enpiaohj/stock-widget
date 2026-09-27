using System.Text.Json;
using StockWidget.Core.Models.Ai;

namespace StockWidget.Core.Services.Ai;

/// <summary>
/// DeepSeek 返回内容 → 结构化分析结果。
/// 容错：剥 Markdown 代码围栏 / 截取首尾大括号间 JSON；字段缺失、null、
/// 非数组一律回落默认值；无法解析返回 null（由调用方转友好错误，不崩 UI）。
/// </summary>
public static class AiResponseParser
{
    public static AiMarketAnalysisResult? Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        var json = ExtractJson(content);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return FromElement(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>剥 ```json 围栏与前后闲话：取首个 { 到最后一个 } 的片段。</summary>
    private static string? ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return content[start..(end + 1)];
    }

    private static AiMarketAnalysisResult FromElement(JsonElement root)
    {
        return new AiMarketAnalysisResult
        {
            OverallState = GetString(root, "overallState"),
            Summary = GetString(root, "summary"),
            TrendAnalysis = GetString(root, "trendAnalysis"),
            VolumeAnalysis = GetString(root, "volumeAnalysis"),
            ResistanceLevels = GetStringList(root, "resistanceLevels"),
            SupportLevels = GetStringList(root, "supportLevels"),
            RiskObservations = GetStringList(root, "riskObservations"),
            Disclaimer = GetString(root, "disclaimer"),
        };
    }

    private static string GetString(JsonElement root, string name)
    {
        if (!TryGetPropertyIgnoreCase(root, name, out var el)) return "";
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    }

    private static List<string> GetStringList(JsonElement root, string name)
    {
        var list = new List<string>();
        if (!TryGetPropertyIgnoreCase(root, name, out var el) || el.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in el.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                list.Add(s);
        return list;
    }

    /// <summary>
    /// 键名风格容错的属性查找：模型返回键名可能是 camelCase / PascalCase / snake_case，
    /// 统一"去除下划线 + ordinal-ignore-case"比较，避免因键名风格差异导致字段解析为空。
    /// </summary>
    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        var normalized = name.Replace("_", "");
        foreach (var prop in root.EnumerateObject())
        {
            if (string.Equals(prop.Name.Replace("_", ""), normalized, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
