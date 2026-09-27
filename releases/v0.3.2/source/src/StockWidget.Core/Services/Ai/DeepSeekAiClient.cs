using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StockWidget.Core.Services.Ai;

/// <summary>AI API 调用异常：Message 为用户可读的中文原因（不含 API Key）。</summary>
public sealed class AiApiException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>AI 分析未配置（未启用 / 无 API Key）时抛出。</summary>
public sealed class AiNotConfiguredException(string message) : Exception(message);

/// <summary>AI Chat Completion 客户端抽象（v0.3.0 仅 DeepSeek 实现）。</summary>
public interface IAiApiClient : IDisposable
{
    /// <summary>发送一次对话补全，返回模型 content 文本。失败抛 AiApiException（用户可读）。</summary>
    Task<string> CompleteAsync(string baseUrl, string apiKey, string model,
        string systemPrompt, string userPrompt, int timeoutSeconds, CancellationToken ct = default);

    /// <summary>获取可用模型 ID 列表（GET {base}/models）。失败抛 AiApiException（用户可读）。</summary>
    Task<List<string>> GetModelsAsync(string baseUrl, string apiKey, int timeoutSeconds, CancellationToken ct = default);
}

/// <summary>DeepSeek chat/completions 客户端（OpenAI 兼容协议）。</summary>
public sealed class DeepSeekAiClient(HttpMessageHandler? handler = null) : IAiApiClient
{
    private readonly HttpClient _http = CreateHttp(handler);

    private static HttpClient CreateHttp(HttpMessageHandler? handler)
    {
        var client = handler is null ? new HttpClient() : new HttpClient(handler);
        client.Timeout = TimeSpan.FromSeconds(120); // 单请求上限；实际以调用方 cts/timeoutSeconds 为准
        return client;
    }

    public async Task<string> CompleteAsync(string baseUrl, string apiKey, string model,
        string systemPrompt, string userPrompt, int timeoutSeconds, CancellationToken ct = default)
    {
        var url = baseUrl.TrimEnd('/') + "/chat/completions";
        var body = JsonSerializer.Serialize(new
        {
            model,
            stream = false,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
                throw MapHttpError((int)resp.StatusCode);

            return ExtractContent(text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 仅超时（调用方未取消）转友好错误；调用方主动取消原样上抛
            throw new AiApiException("AI 请求超时，请稍后重试或增大超时时间。");
        }
        catch (HttpRequestException)
        {
            throw new AiApiException("网络连接失败，请检查网络或 API 地址。");
        }
    }

    private static AiApiException MapHttpError(int status) => status switch
    {
        400 => new AiApiException("HTTP 400：请求参数错误。", status),
        401 => new AiApiException("HTTP 401：API Key 无效或无权限。", status),
        403 => new AiApiException("HTTP 403：无权限访问该资源。", status),
        404 => new AiApiException("HTTP 404：模型或 API 地址不存在。", status),
        429 => new AiApiException("HTTP 429：请求过于频繁或当前额度受限。", status),
        _ => new AiApiException($"HTTP {status}：DeepSeek 服务端错误，请稍后重试。", status),
    };

    public async Task<List<string>> GetModelsAsync(string baseUrl, string apiKey, int timeoutSeconds, CancellationToken ct = default)
    {
        var url = baseUrl.TrimEnd('/') + "/models";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 300)));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw MapHttpError((int)resp.StatusCode);

            var text = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                        models.Add(id.GetString() ?? "");
            }
            return models.Where(m => m.Length > 0).Distinct().ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AiApiException("获取模型列表超时，请稍后重试。");
        }
        catch (HttpRequestException)
        {
            throw new AiApiException("网络连接失败，请检查网络或 API 地址。");
        }
        catch (AiApiException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AiApiException("模型列表返回格式无法解析。");
        }
    }

    private static string ExtractContent(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0)
                throw new AiApiException("AI 模型返回为空。");
            return choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (AiApiException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AiApiException("AI 返回格式无法解析。");
        }
    }

    public void Dispose() => _http.Dispose();
}
