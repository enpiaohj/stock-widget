using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StockWidget.Core.Data;

namespace StockWidget.Core;

/// <summary>dotnet-ef 设计时工厂（生成迁移用，不参与运行时）。</summary>
public sealed class StockWidgetDbContextFactory : IDesignTimeDbContextFactory<StockWidgetDbContext>
{
    public StockWidgetDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StockWidgetDbContext>()
            .UseSqlite($"Data Source={DbPathResolver.GetDatabasePath()}")
            .Options;
        return new StockWidgetDbContext(options);
    }
}
