using System.Text.Json;
using StockWidget.Core.Models.Ai;

namespace StockWidget.Core.Services.Ai;

/// <summary>AI 行情分析服务。</summary>
public interface IAiAnalysisService
{
    /// <summary>
    /// 基于本地行情上下文生成 AI 分析。缓存优先（Key=代码+最后K线日+最新收盘+模型）；
    /// forceRefresh=true 跳过缓存强制重新请求。未启用/未配置 Key 抛 AiNotConfiguredException。
    /// </summary>
    Task<AiMarketAnalysisResult> AnalyzeAsync(
        MarketAnalysisContext context, bool forceRefresh = false, CancellationToken ct = default);
}

/// <summary>DeepSeek 实现：缓存 → Prompt → API → 容错解析 → 写缓存。</summary>
public sealed class DeepSeekAiAnalysisService(
    IAiApiClient client,
    IAiAnalysisCacheRepository cache,
    ISettingsService settings) : IAiAnalysisService
{
    private static readonly JsonSerializerOptions CacheJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<AiMarketAnalysisResult> AnalyzeAsync(
        MarketAnalysisContext context, bool forceRefresh = false, CancellationToken ct = default)
    {
        var ai = settings.Current.Ai;
        if (!ai.Enabled)
            throw new AiNotConfiguredException("AI 分析未启用，请先在 设置 → AI 分析 中启用并配置 DeepSeek API。");
        var apiKey = AiCredentialProtector.Unprotect(ai.ApiKeyEncrypted);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiNotConfiguredException("尚未配置 AI 分析，请先在 设置 → AI 分析 配置 DeepSeek API Key。");

        var last = context.Klines.Count > 0 ? context.Klines[^1] : null;
        // Key 含 LastVolume：盘中未收盘时 Close/Volume 持续变化，避免错误复用旧分析
        var cacheKey = $"{context.Code}|{last?.Date ?? ""}|{last?.Close ?? 0}|{last?.Volume ?? 0}|{ai.Model}";

        if (!forceRefresh)
        {
            var cachedJson = cache.GetResultJson(cacheKey);
            var cached = cachedJson is null ? null : AiResponseParser.Parse(cachedJson);
            if (cached is not null) return cached;
        }

        var userPrompt = AiPromptBuilder.BuildUserPrompt(context);
        var content = await client.CompleteAsync(
            ai.BaseUrl, apiKey, ai.Model, AiPromptBuilder.SystemPrompt, userPrompt, ai.TimeoutSeconds, ct)
            .ConfigureAwait(false);

        // DEBUG 编译下保存原始响应（脱敏：模型分析文本，不含 Key / 请求头）
        AiDebugLog.Append($"{context.Code} {ai.Model}", content);

        var result = AiResponseParser.Parse(content)
            ?? throw new AiApiException("AI 返回内容无法解析为有效 JSON，请点击重新分析重试。");

        cache.Upsert(cacheKey, JsonSerializer.Serialize(result, CacheJson));
        return result;
    }
}
