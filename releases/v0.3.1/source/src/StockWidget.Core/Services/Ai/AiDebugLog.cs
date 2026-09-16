using System.IO;

namespace StockWidget.Core.Services.Ai;

/// <summary>
/// AI 原始响应调试日志（仅 DEBUG 编译生效）：
/// 保存 DeepSeek 返回的原始 content 到 %LOCALAPPDATA%\StockWidget\ai_debug.log，
/// 用于确认模型实际返回结构。内容为模型生成的市场分析文本，不含 API Key / 请求头等敏感信息。
/// Release 编译不包含任何调用。
/// </summary>
public static class AiDebugLog
{
    public static void Append(string tag, string content)
    {
#if DEBUG
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StockWidget");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ai_debug.log");

            // 超 1MB 重建，避免无限增长
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.Delete(path);

            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{tag}]\n{content}\n---\n");
        }
        catch
        {
            // 调试日志失败静默
        }
#endif
    }
}
