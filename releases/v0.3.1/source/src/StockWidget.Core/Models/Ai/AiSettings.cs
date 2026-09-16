namespace StockWidget.Core.Models.Ai;

/// <summary>
/// AI 分析配置（随 AppSettings 整体序列化存 SQLite settings 表）。
/// API Key 仅存 DPAPI（CurrentUser）加密后的 Base64 密文，不落明文。
/// </summary>
public sealed class AiSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>API Base URL（不含 /chat/completions 路径）。</summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>模型名（用户可改，不硬绑具体模型）。</summary>
    public string Model { get; set; } = "deepseek-flash";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>DPAPI 加密后的 API Key（Base64 密文）；空 = 未配置。</summary>
    public string ApiKeyEncrypted { get; set; } = "";

    public AiSettings Clone() => (AiSettings)MemberwiseClone();
}
