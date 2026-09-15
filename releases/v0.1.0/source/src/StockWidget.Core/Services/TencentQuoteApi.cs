using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

// ---------------------------
// 腾讯行情 API（qt.gtimg.cn + 分时 ifzq.gtimg.cn）
// ---------------------------
public interface ITencentQuoteApi
{
    /// <summary>批量抓取行情（单请求多代码）。失败的代码返回 Success=false 的占位项。</summary>
    Task<List<QuoteData>> FetchQuotesAsync(IReadOnlyList<string> codes, CancellationToken ct = default);

    /// <summary>当日分时（用于迷你走势图 / 分时弹窗）；接口失败返回 null。</summary>
    Task<MinuteLineData?> FetchMinuteLineAsync(string code, CancellationToken ct = default);
}

/// <summary>分时数据：HHmm 时间点 + 价格序列。</summary>
public sealed record MinuteLineData
{
    public string Code { get; init; } = "";
    public string Date { get; init; } = "";
    public List<MinutePoint> Points { get; init; } = [];
    public decimal? PrevClose { get; init; }
}

public sealed record MinutePoint(string Time, decimal Price);

public sealed class TencentQuoteApi : ITencentQuoteApi, IDisposable
{
    private const string QuoteUrl = "https://qt.gtimg.cn/q=";
    private const string MinuteUrl = "https://web.ifzq.gtimg.cn/appstock/app/minute/query?code=";

    private readonly HttpClient _http;

    static TencentQuoteApi()
    {
        // GBK 解码支持（腾讯接口为 GBK 编码）
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public TencentQuoteApi()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 8,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
    }

    public async Task<List<QuoteData>> FetchQuotesAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
    {
        var result = new List<QuoteData>(codes.Count);
        if (codes.Count == 0) return result;

        // 旧版口径：单只 4s 超时 + 最多 2 次重试
        for (var attempt = 0; attempt <= 2; attempt++)
        {
            try
            {
                var url = QuoteUrl + string.Join(",", codes);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));
                using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
                var text = Encoding.GetEncoding("GBK").GetString(bytes);
                var parsed = TencentResponseParser.ParseBatch(text, codes);
                if (parsed.Count > 0 || attempt == 2)
                {
                    // 补齐未返回的代码为失败占位
                    var got = parsed.Select(p => p.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var code in codes.Where(c => !got.Contains(c)))
                        parsed.Add(QuoteData.Failed(code));
                    return parsed;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超时，重试
            }
            catch (HttpRequestException)
            {
                // 网络/代理错误，重试
            }
            catch (Exception)
            {
                if (attempt == 2) break;
            }

            await Task.Delay(250 * (attempt + 1), ct).ConfigureAwait(false);
        }

        return codes.Select(QuoteData.Failed).ToList();
    }

    public async Task<MinuteLineData?> FetchMinuteLineAsync(string code, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var resp = await _http.GetAsync(MinuteUrl + code, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            return TencentResponseParser.ParseMinute(doc.RootElement, code);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

// ---------------------------
// 响应解析（字段下标与旧版完全一致）
// ---------------------------
public static class TencentResponseParser
{
    /// <summary>
    /// 解析 qt.gtimg.cn 响应（GBK 已解码）。
    /// 格式：v_sh600390="1~浦发银行~600390~10.74~…"; 每段以 ; 分隔。
    /// </summary>
    public static List<QuoteData> ParseBatch(string text, IReadOnlyList<string> requestedCodes)
    {
        var result = new List<QuoteData>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var rawLine in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = rawLine.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0) continue;

            var varName = rawLine[..eq].Trim();
            var code = varName.StartsWith("v_", StringComparison.OrdinalIgnoreCase) ? varName[2..] : varName;
            var requested = requestedCodes.FirstOrDefault(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
            if (requested is null) continue;

            var payload = rawLine[(eq + 1)..].Trim();
            if (payload.Length >= 2 && payload[0] == '"' && payload[^1] == '"')
                payload = payload[1..^1];
            var parts = payload.Split('~');

            var quote = ParseOne(requested, parts);
            if (quote is not null) result.Add(quote);
        }

        return result;
    }

    /// <summary>
    /// 单只解析；名称或现价缺失视为失败。
    /// 字段下标与旧版完全一致：1名称 3现价 32涨跌幅 6成交量 38换手率 33最高 34最低 37成交额 5开盘 4昨收 44市值 43振幅。
    /// </summary>
    public static QuoteData? ParseOne(string code, string[] parts)
    {
        if (parts.Length < 5) return null;

        var name = parts[1];
        var price = StockCodeNormalizer.ParseDecimal(parts[3]);
        if (string.IsNullOrWhiteSpace(name) || price is null) return null;

        decimal? Get(int i) => i < parts.Length ? StockCodeNormalizer.ParseDecimal(parts[i]) : null;

        return new QuoteData
        {
            Code = code,
            Name = name,
            Price = price,
            Success = true,
            FetchedAt = DateTime.Now,
            ChangePct = Get(32),
            Volume = Get(6),
            Turnover = Get(38),
            High = Get(33),
            Low = Get(34),
            Amount = Get(37),
            Open = Get(5),
            PrevClose = Get(4),
            MarketCap = Get(44),
            Amplitude = Get(43),
        };
    }

    /// <summary>解析分时 JSON（web.ifzq.gtimg.cn /appstock/app/minute/query）。</summary>
    public static MinuteLineData? ParseMinute(JsonElement root, string code)
    {
        try
        {
            if (root.ValueKind != JsonValueKind.Object || root.GetProperty("code").GetInt32() != 0)
                return null;

            var dataRoot = root.GetProperty("data");
            var entry = dataRoot.GetProperty(code);
            var data = entry.GetProperty("data");
            var rows = data.GetProperty("data");

            var points = new List<MinutePoint>(rows.GetArrayLength());
            foreach (var row in rows.EnumerateArray())
            {
                var s = row.GetString();
                if (string.IsNullOrEmpty(s)) continue;
                var seg = s.Split(' ');
                if (seg.Length < 2) continue;
                if (decimal.TryParse(seg[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    points.Add(new MinutePoint(seg[0], p));
            }

            decimal? prevClose = null;
            if (entry.TryGetProperty("qt", out var qt))
            {
                var qtKey = "v_" + code;
                if (qt.TryGetProperty(qtKey, out var qtArr) && qtArr.ValueKind == JsonValueKind.Array
                    && qtArr.GetArrayLength() > 4)
                {
                    prevClose = StockCodeNormalizer.ParseDecimal(qtArr[4].GetString());
                }
            }

            string dateStr = "";
            if (data.TryGetProperty("date", out var dateEl) && dateEl.ValueKind == JsonValueKind.String)
                dateStr = dateEl.GetString() ?? "";

            return new MinuteLineData { Code = code, Date = dateStr, Points = points, PrevClose = prevClose };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
