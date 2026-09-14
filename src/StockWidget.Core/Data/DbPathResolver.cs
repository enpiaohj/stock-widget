using System.IO;

namespace StockWidget.Core.Data;

/// <summary>
/// 数据库文件路径解析：便携优先（exe 目录下 data/），无写权限时回退 %APPDATA%\StockWidget\。
/// 结果在进程内缓存。
/// </summary>
public static class DbPathResolver
{
    private static string? _cachedDir;

    public static void ResetForTest() => _cachedDir = null;

    /// <summary>数据目录（含尾部分隔符），确定后进程内不变。</summary>
    public static string GetDataDirectory()
    {
        if (_cachedDir != null) return _cachedDir;

        // 单文件发布后 AppContext.BaseDirectory 即 exe 所在目录
        var portable = Path.Combine(AppContext.BaseDirectory, "data");
        try
        {
            Directory.CreateDirectory(portable);
            var probe = Path.Combine(portable, $".write_test_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            _cachedDir = portable;
        }
        catch (UnauthorizedAccessException)
        {
            _cachedDir = GetAppDataDir();
        }
        catch (IOException)
        {
            _cachedDir = GetAppDataDir();
        }

        return _cachedDir;
    }

    private static string GetAppDataDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StockWidget");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>SQLite 数据库文件完整路径。</summary>
    public static string GetDatabasePath() => Path.Combine(GetDataDirectory(), "stockwidget.db");
}
