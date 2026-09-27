using System.Net;
using System.Text;
using StockWidget.Core.Services.Ai;

namespace StockWidget.Tests;

/// <summary>DeepSeek HTTP 客户端：请求构造与错误映射。</summary>
public class DeepSeekAiClientTests
{
    /// <summary>可编程响应的 HttpMessageHandler。</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(ct);
            return await responder(request, ct);
        }
    }

    private static HttpResponseMessage JsonOk(string content)
    {
        var body = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":"
                   + System.Text.Json.JsonSerializer.Serialize(content) + "}}]}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    private const string SystemPrompt = "system-prompt";
    private const string UserPrompt = """{"code":"sh000001"}""";

    private static DeepSeekAiClient MakeClient(FakeHandler handler) => new(handler);

    [Fact]
    public async Task Success_ReturnsContent()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(JsonOk("分析内容")));
        var client = MakeClient(handler);

        var content = await client.CompleteAsync(
            "https://api.deepseek.com", "sk-key", "deepseek-chat", SystemPrompt, UserPrompt, 60, CancellationToken.None);

        Assert.Equal("分析内容", content);
    }

    [Fact]
    public async Task Request_HasAuthHeader_ModelAndMessages()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(JsonOk("ok")));
        var client = MakeClient(handler);

        await client.CompleteAsync("https://api.deepseek.com", "sk-secret", "deepseek-chat", SystemPrompt, UserPrompt, 60, CancellationToken.None);

        Assert.Equal("Bearer sk-secret", handler.LastRequest!.Headers.Authorization!.ToString());
        Assert.EndsWith("/chat/completions", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"model\":\"deepseek-chat\"", handler.LastBody);
        Assert.Contains("system-prompt", handler.LastBody);
        Assert.Contains("code", handler.LastBody);
    }

    [Fact]
    public async Task Request_TrailingSlashBaseUrl_NoDoubleSlash()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(JsonOk("ok")));
        await MakeClient(handler).CompleteAsync("https://api.deepseek.com/", "k", "m", "s", "u", 60, CancellationToken.None);
        Assert.Contains("https://api.deepseek.com/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(401, "API Key 无效或无权限")]
    [InlineData(403, "无权限访问")]
    [InlineData(404, "模型或 API 地址不存在")]
    [InlineData(429, "请求过于频繁或当前额度受限")]
    [InlineData(400, "请求参数错误")]
    [InlineData(500, "DeepSeek 服务端错误")]
    public async Task HttpError_MapsToFriendlyMessage(int status, string expected)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).CompleteAsync("https://api.deepseek.com", "k", "m", "s", "u", 60, CancellationToken.None));

        Assert.Equal(status, ex.StatusCode);
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task NetworkFailure_MapsToFriendlyMessage()
    {
        var handler = new FakeHandler((_, _) => throw new HttpRequestException("conn refused"));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).CompleteAsync("https://api.deepseek.com", "k", "m", "s", "u", 60, CancellationToken.None));

        Assert.Contains("网络连接失败", ex.Message);
    }

    [Fact]
    public async Task Timeout_MapsToFriendlyMessage()
    {
        var handler = new FakeHandler((_, _) => throw new TaskCanceledException("timed out"));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).CompleteAsync("https://api.deepseek.com", "k", "m", "s", "u", 60, CancellationToken.None));

        Assert.Contains("请求超时", ex.Message);
    }

    [Fact]
    public async Task EmptyChoices_MapsToFriendlyMessage()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"choices":[]}""", Encoding.UTF8, "application/json"),
        }));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).CompleteAsync("https://api.deepseek.com", "k", "m", "s", "u", 60, CancellationToken.None));

        Assert.Contains("模型返回为空", ex.Message);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(200, token); // 异步等待，响应 cts 取消
            return JsonOk("late");
        });
        var client = MakeClient(handler);
        using var cts = new CancellationTokenSource(50);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CompleteAsync("https://api.deepseek.com", "k", "m", "s", "u", 60, cts.Token));
    }

    [Fact]
    public async Task GetModels_ReturnsIds()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"data":[{"id":"deepseek-flash"},{"id":"deepseek-chat"},{"id":"deepseek-reasoner"}]}""",
                Encoding.UTF8, "application/json"),
        }));

        var models = await MakeClient(handler).GetModelsAsync("https://api.deepseek.com", "sk-key", 60, CancellationToken.None);

        Assert.Equal(3, models.Count);
        Assert.Contains("deepseek-flash", models);
    }

    [Fact]
    public async Task GetModels_EmptyData_ReturnsEmpty()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":[]}""", Encoding.UTF8, "application/json"),
        }));

        var models = await MakeClient(handler).GetModelsAsync("https://api.deepseek.com", "sk-key", 60, CancellationToken.None);
        Assert.Empty(models);
    }

    [Fact]
    public async Task GetModels_HttpError_MapsToFriendlyMessage()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).GetModelsAsync("https://api.deepseek.com", "bad", 60, CancellationToken.None));

        Assert.Contains("API Key 无效或无权限", ex.Message);
    }

    [Fact]
    public async Task ErrorBody_DoesNotLeakApiKey()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var ex = await Assert.ThrowsAsync<AiApiException>(() =>
            MakeClient(handler).CompleteAsync("https://api.deepseek.com", "sk-very-secret", "m", "s", "u", 60, CancellationToken.None));

        Assert.DoesNotContain("sk-very-secret", ex.Message);
    }
}
