using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core;
using StockWidget.Core.Data;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

/// <summary>测试基类：提供指向临时 SQLite 文件的完整 DI 容器。</summary>
public abstract class DatabaseTestBase : IDisposable
{
    private readonly string _dbPath;

    protected IServiceProvider Provider { get; }
    protected string TempDirectory { get; }

    protected DatabaseTestBase()
    {
        TempDirectory = Path.Combine(Path.GetTempPath(), "stockwidget_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDirectory);
        _dbPath = Path.Combine(TempDirectory, "test.db");

        var services = new ServiceCollection();
        services.AddStockWidgetCore(_dbPath);
        Provider = services.BuildServiceProvider();

        // 让 DbPathResolver 不干扰（测试直接注入 dbPath）
        DbPathResolver.ResetForTest();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(TempDirectory)) Directory.Delete(TempDirectory, true);
        }
        catch
        {
            // 临时目录清理失败可忽略
        }
        GC.SuppressFinalize(this);
    }
}
