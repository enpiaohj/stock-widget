using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

// ---------------------------
// 设置服务
// ---------------------------
public interface ISettingsService
{
    AppSettings Current { get; }
    /// <summary>从数据库重新加载（丢弃未保存修改）。</summary>
    AppSettings Reload();
    /// <summary>保存当前设置到数据库并更新 Current。</summary>
    void Save(AppSettings settings);
    /// <summary>持久化 Current 的当前值（轻量更新场景）。</summary>
    void SaveCurrent();
}

public sealed class SettingsService(IDbContextFactory<StockWidgetDbContext> dbFactory) : ISettingsService
{
    private const string SettingsKey = "app";
    private readonly object _lock = new();
    private AppSettings? _current;

    public AppSettings Current
    {
        get { lock (_lock) { return _current ??= LoadFromDb(); } }
    }

    public AppSettings Reload()
    {
        lock (_lock)
        {
            _current = LoadFromDb();
            return _current.Clone();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            Upsert(db, settings);
            _current = settings.Clone();
        }
    }

    public void SaveCurrent()
    {
        lock (_lock)
        {
            using var db = dbFactory.CreateDbContext();
            Upsert(db, _current ?? LoadFromDb());
        }
    }

    private AppSettings LoadFromDb()
    {
        using var db = dbFactory.CreateDbContext();
        var row = db.Settings.Find(SettingsKey);
        if (row is null)
        {
            var def = new AppSettings();
            Upsert(db, def);
            return def;
        }

        try
        {
            var loaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(row.Value) ?? new AppSettings();
            // 兼容字段宽度：确保必选字段存在
            foreach (var (disp, key, w, _) in FieldDefinitions.All)
                loaded.FieldWidths.TryAdd(key, w);
            // 兜底：CustomFields 为空（误操作/旧版导入）时回退默认，避免只显示必选列
            if (loaded.CustomFields.Count == 0)
                loaded.CustomFields = FieldDefinitions.All
                    .Where(f => !f.Required)
                    .Select(f => f.Key)
                    .ToList();
            return loaded;
        }
        catch (System.Text.Json.JsonException)
        {
            // 配置损坏时回退默认值（不覆盖库里数据，等待下次 Save 覆盖）
            return new AppSettings();
        }
    }

    private static void Upsert(StockWidgetDbContext db, AppSettings settings)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var row = db.Settings.Find(SettingsKey);
        if (row is null)
            db.Settings.Add(new SettingEntity { Key = SettingsKey, Value = json });
        else
            row.Value = json;
        db.SaveChanges();
    }
}

// ---------------------------
// 字段定义（与旧版 ALL_FIELDS 对齐：显示名, key, 默认宽, 必选）
// ---------------------------
public static class FieldDefinitions
{
    public sealed record FieldDef(string Display, string Key, int DefaultWidth, bool Required);

    public static readonly FieldDef[] All =
    [
        new("名称", "name", 14, true),
        new("现价", "price", 12, true),
        new("涨跌幅", "change", 10, true),
        new("成交量", "volume", 10, false),
        new("成交额", "amount", 10, false),
        new("换手率", "turnover", 10, false),
        new("最高", "high", 12, false),
        new("最低", "low", 12, false),
        new("开盘", "open", 12, false),
        new("昨收", "prev_close", 12, false),
        new("市值", "market_cap", 12, false),
        new("振幅", "amplitude", 10, true),
    ];

    public static FieldDef? Find(string key) =>
        Array.Find(All, f => f.Key == key);

    /// <summary>列宽字符单位 → 像素（旧版口径：单位*8+12，钳制 [30,800]）。</summary>
    public static int WidthToPixels(int units) =>
        Math.Clamp(units * 8 + 12, 30, 800);
}
