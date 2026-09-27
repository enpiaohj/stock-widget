using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;

namespace StockWidget.Core.Services.Ai;

/// <summary>AI 分析结果缓存仓储（Key=代码+最后K线日+最新收盘+模型）。</summary>
public interface IAiAnalysisCacheRepository
{
    string? GetResultJson(string key);
    void Upsert(string key, string resultJson);
}

public sealed class AiAnalysisCacheRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IAiAnalysisCacheRepository
{
    public string? GetResultJson(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        using var db = dbFactory.CreateDbContext();
        return db.AiAnalysisCaches.AsNoTracking()
            .Where(c => c.Key == key)
            .Select(c => c.ResultJson)
            .FirstOrDefault();
    }

    public void Upsert(string key, string resultJson)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(resultJson)) return;
        using var db = dbFactory.CreateDbContext();
        var row = db.AiAnalysisCaches.Find(key);
        if (row is null)
        {
            db.AiAnalysisCaches.Add(new AiAnalysisCacheEntity
            {
                Key = key,
                ResultJson = resultJson,
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }
        else
        {
            row.ResultJson = resultJson;
            row.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
        db.SaveChanges();
    }
}
