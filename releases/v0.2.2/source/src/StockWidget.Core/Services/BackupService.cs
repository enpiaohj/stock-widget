using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

/// <summary>数据备份导入导出结果。</summary>
public sealed record BackupResult(bool Success, string Message, int Settings, int Watchlist, int DailyDays, int AlertRules)
{
    public static BackupResult Fail(string error) => new(false, error, 0, 0, 0, 0);
}

public interface IBackupService
{
    /// <summary>导出全部用户数据为 JSON 备份文件。</summary>
    Task<BackupResult> ExportAsync(string path, CancellationToken ct = default);
    /// <summary>从备份文件合并导入（设置覆盖、其余按 key 合并去重）。</summary>
    Task<BackupResult> ImportAsync(string path, CancellationToken ct = default);
}

/// <summary>数据备份（JSON）：设置 + 自选股 + 每日成交额历史 + 预警规则。</summary>
public sealed class BackupService(IDbContextFactory<StockWidgetDbContext> dbFactory) : IBackupService
{
    private const string BackupType = "stockwidget-backup";
    private const int BackupVersion = 1;

    // 备份文件结构（version 1）
    private sealed record BackupFile(string Type, int Version, string ExportedAt,
        AppSettings? Settings, List<WatchlistEntity>? Watchlist,
        List<DailyAmountEntity>? DailyAmount, List<AlertRuleEntity>? AlertRules);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task<BackupResult> ExportAsync(string path, CancellationToken ct = default)
    {
        try
        {
            using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var settings = await db.Settings.ToListAsync(ct).ConfigureAwait(false);
            var watchlist = await db.Watchlist.AsNoTracking().OrderBy(w => w.SortOrder).ToListAsync(ct).ConfigureAwait(false);
            var daily = await db.DailyAmountHistory.AsNoTracking().OrderBy(d => d.Date).ToListAsync(ct).ConfigureAwait(false);
            var alerts = await db.AlertRules.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

            var file = new BackupFile(BackupType, BackupVersion, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                JsonSerializer.Deserialize<AppSettings>(settings.FirstOrDefault(s => s.Key == "app")?.Value ?? "{}"),
                watchlist, daily, alerts);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, JsonOpts), Encoding.UTF8, ct).ConfigureAwait(false);

            return new BackupResult(true, $"导出完成：自选股 {watchlist.Count}，历史 {daily.Count} 天，预警 {alerts.Count} 条。",
                1, watchlist.Count, daily.Count, alerts.Count);
        }
        catch (Exception ex)
        {
            return BackupResult.Fail($"导出失败：{ex.Message}");
        }
    }

    public async Task<BackupResult> ImportAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize<BackupFile>(json, JsonOpts);
            if (file is null || file.Type != BackupType)
                return BackupResult.Fail("导入失败：不是有效的 StockWidget 备份文件。");

            using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            int wlNew = 0, dayNew = 0, dayUpdated = 0, alertNew = 0;

            // 设置：整体覆盖
            if (file.Settings is { } s)
            {
                var row = await db.Settings.FirstOrDefaultAsync(x => x.Key == "app", ct).ConfigureAwait(false);
                var value = JsonSerializer.Serialize(s);
                if (row is null) db.Settings.Add(new SettingEntity { Key = "app", Value = value });
                else row.Value = value;
            }

            // 自选股：按代码去重追加
            if (file.Watchlist is { Count: > 0 } wl)
            {
                var existing = (await db.Watchlist.ToListAsync(ct).ConfigureAwait(false))
                    .Select(w => w.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var maxOrder = existing.Count == 0 ? -1 : await db.Watchlist.MaxAsync(w => w.SortOrder, ct).ConfigureAwait(false);
                foreach (var w in wl.Where(w => !existing.Contains(w.Code)))
                {
                    maxOrder++;
                    db.Watchlist.Add(new WatchlistEntity
                    {
                        Code = w.Code, Name = w.Name, SortOrder = maxOrder, Group = w.Group, IsPinned = w.IsPinned,
                    });
                    wlNew++;
                }
            }

            // 历史成交额：按日期 upsert
            if (file.DailyAmount is { Count: > 0 } days)
            {
                var dates = days.Select(d => d.Date).ToHashSet();
                var existingDates = (await db.DailyAmountHistory.ToListAsync(ct).ConfigureAwait(false))
                    .Where(d => dates.Contains(d.Date)).Select(d => d.Date).ToHashSet();
                foreach (var d in days)
                {
                    if (existingDates.Contains(d.Date))
                    {
                        var row = await db.DailyAmountHistory.FirstAsync(x => x.Date == d.Date, ct).ConfigureAwait(false);
                        row.ShAmount = d.ShAmount; row.SzAmount = d.SzAmount; row.Total = d.Total;
                        dayUpdated++;
                    }
                    else
                    {
                        db.DailyAmountHistory.Add(new DailyAmountEntity
                            { Date = d.Date, ShAmount = d.ShAmount, SzAmount = d.SzAmount, Total = d.Total });
                        dayNew++;
                    }
                }
            }

            // 预警规则：按代码+方向去重追加
            if (file.AlertRules is { Count: > 0 } rules)
            {
                var existing = (await db.AlertRules.ToListAsync(ct).ConfigureAwait(false))
                    .Select(a => (a.Code, a.Direction)).ToHashSet();
                foreach (var a in rules.Where(a => !existing.Contains((a.Code, a.Direction))))
                {
                    db.AlertRules.Add(new AlertRuleEntity
                    {
                        Code = a.Code, StockName = a.StockName, ThresholdPct = a.ThresholdPct,
                        Direction = a.Direction, Enabled = a.Enabled, LastTriggeredAt = null,
                    });
                    alertNew++;
                }
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new BackupResult(true,
                $"导入完成：自选股新增 {wlNew}，历史新增 {dayNew} 天、更新 {dayUpdated} 天，预警新增 {alertNew}。设置已覆盖（重启后完全生效）。",
                file.Settings is null ? 0 : 1, wlNew, dayNew + dayUpdated, alertNew);
        }
        catch (Exception ex)
        {
            return BackupResult.Fail($"导入失败：{ex.Message}");
        }
    }
}
