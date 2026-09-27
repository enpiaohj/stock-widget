using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockWidget.Core.Data;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

// ---------------------------
// 旧版（Python）数据导入：stock_config.json / history_amount.json
// ---------------------------
public sealed record LegacyImportResult
{
    public bool ConfigImported { get; init; }
    public bool HistoryImported { get; init; }
    public int StocksImported { get; init; }
    public int HistoryDays { get; init; }
    public string? Error { get; init; }

    public bool HasAnything => ConfigImported || HistoryImported;
}

public interface ILegacyImporter
{
    /// <summary>扫描目录中的旧版文件并导入（已导入过则跳过）。</summary>
    LegacyImportResult ImportFromDirectory(string directory);

    /// <summary>是否已导入过旧版数据（settings 表标记）。</summary>
    bool IsLegacyImported();
}

public sealed class LegacyImporter(
    IDbContextFactory<StockWidgetDbContext> dbFactory,
    ISettingsService settings,
    IWatchlistRepository watchlist,
    IAmountHistoryRepository history) : ILegacyImporter
{
    private const string ImportedFlagKey = "legacy_imported";

    public bool IsLegacyImported()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Settings.Find(ImportedFlagKey) is not null;
    }

    public LegacyImportResult ImportFromDirectory(string directory)
    {
        var result = new LegacyImportResult();
        try
        {
            if (IsLegacyImported())
                return result with { Error = "旧版数据已导入过，跳过。" };

            var configPath = Path.Combine(directory, "stock_config.json");
            var historyPath = Path.Combine(directory, "history_amount.json");

            if (File.Exists(configPath))
                result = ImportConfig(configPath, result);
            if (File.Exists(historyPath))
                result = ImportHistory(historyPath, result);

            if (result.HasAnything)
            {
                using var db = dbFactory.CreateDbContext();
                db.Settings.Add(new SettingEntity { Key = ImportedFlagKey, Value = DateTime.Now.ToString("s") });
                db.SaveChanges();
            }

            return result;
        }
        catch (Exception ex)
        {
            return result with { Error = ex.Message };
        }
    }

    private LegacyImportResult ImportConfig(string path, LegacyImportResult result)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var cfg = settings.Current.Clone();

            if (root.ValueKind == JsonValueKind.Array)
            {
                // 最老格式：根数组即自选股列表
                AddLegacyStocks(root);
                result = result with { ConfigImported = true, StocksImported = root.GetArrayLength() };
                return result;
            }

            if (root.ValueKind != JsonValueKind.Object) return result;

            if (root.TryGetProperty("stocks", out var stocks) && stocks.ValueKind == JsonValueKind.Array)
            {
                AddLegacyStocks(stocks);
                result = result with { StocksImported = stocks.GetArrayLength() };
            }

            // 逐项映射（键与旧版一致）
            if (TryGetBool(root, "locked", out var v)) cfg.Locked = v;
            if (TryGetString(root, "theme", out var t) && t is "dark" or "light") cfg.Theme = t;
            if (TryGetInt(root, "refresh_interval", out var r)) cfg.RefreshIntervalMs = Math.Clamp(r, 1000, 120000);
            if (TryGetInt(root, "opacity", out var o)) cfg.OpacityPercent = Math.Clamp(o * 10, 10, 100);
            if (TryGetString(root, "default_hotkey", out var h) && h.Contains('+')) cfg.Hotkey = h.ToLowerInvariant();
            if (TryGetBool(root, "font_italic", out var fi)) cfg.FontItalic = fi;
            if (TryGetInt(root, "font_size", out var fs)) cfg.FontSize = Math.Clamp(fs, 6, 28);
            if (TryGetString(root, "font_family", out var ff) && ff.Length > 0) cfg.FontFamily = ff;
            if (TryGetBool(root, "show_total_amount", out var sta)) cfg.ShowTotalAmount = sta;
            if (TryGetBool(root, "show_refresh_interval", out var sri)) cfg.ShowRefreshInterval = sri;
            if (TryGetBool(root, "show_locked_status", out var sls)) cfg.ShowLockedStatus = sls;
            if (TryGetBool(root, "show_update_weekday", out var suw)) cfg.ShowUpdateWeekday = suw;
            if (TryGetBool(root, "show_update_week_number", out var suwn)) cfg.ShowUpdateWeekNumber = suwn;
            if (TryGetInt(root, "yesterday_amount", out _)) { /* 旧版临时值，不迁移 */ }

            if (root.TryGetProperty("window_position", out var wp) && wp.ValueKind == JsonValueKind.Object)
            {
                if (wp.TryGetProperty("x", out var x)) cfg.WindowX = x.GetInt32();
                if (wp.TryGetProperty("y", out var y)) cfg.WindowY = y.GetInt32();
            }

            if (root.TryGetProperty("custom_fields", out var cf) && cf.ValueKind == JsonValueKind.Array)
            {
                // 旧版用显示名，转为字段 key
                var keys = new List<string>();
                foreach (var item in cf.EnumerateArray())
                {
                    var disp = item.GetString();
                    var def = FieldDefinitions.All.FirstOrDefault(f => f.Display == disp);
                    if (def is not null && !def.Required) keys.Add(def.Key);
                }
                if (keys.Count > 0) cfg.CustomFields = keys;
            }

            if (root.TryGetProperty("field_widths", out var fw) && fw.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fw.EnumerateObject())
                {
                    var def = FieldDefinitions.All.FirstOrDefault(f => f.Display == prop.Name || f.Key == prop.Name);
                    if (def is null) continue;
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var w))
                        cfg.FieldWidths[def.Key] = Math.Clamp(w, 6, 100);
                }
            }

            settings.Save(cfg);
            return result with { ConfigImported = true };
        }
        catch (Exception ex)
        {
            return result with { Error = $"导入 stock_config.json 失败：{ex.Message}" };
        }
    }

    private LegacyImportResult ImportHistory(string path, LegacyImportResult result)
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;

            var days = 0;
            foreach (var day in doc.RootElement.EnumerateObject())
            {
                if (day.Value.ValueKind != JsonValueKind.Object) continue;
                if (!DateOnly.TryParseExact(day.Name, "yyyy-MM-dd", out _)) continue;

                decimal sh = GetAmount(day.Value, "sh000001");
                decimal sz = GetAmount(day.Value, "sz399001");
                decimal total;
                if (day.Value.TryGetProperty("total_amount", out var ta) && ta.TryGetDecimal(out var t))
                    total = t;
                else
                    total = sh + sz;
                if (total <= 0) continue;

                history.Upsert(day.Name, sh, sz);
                days++;
            }

            return result with { HistoryImported = true, HistoryDays = days };
        }
        catch (Exception ex)
        {
            return result with { Error = $"导入 history_amount.json 失败：{ex.Message}" };
        }
    }

    private void AddLegacyStocks(JsonElement stocks)
    {
        var items = new List<StockItem>();
        var order = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stocks.EnumerateArray())
        {
            var raw = s.GetString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var code = StockCodeNormalizer.Normalize(raw);
            if (code.Length == 0 || !seen.Add(code)) continue;
            items.Add(new StockItem
            {
                Code = code,
                Name = "",
                SortOrder = order++,
                IsPinned = code is MainIndexCodes.Shanghai or MainIndexCodes.Shenzhen,
            });
        }

        if (items.Count > 0)
            watchlist.ReplaceAll(items);
    }

    private static decimal GetAmount(JsonElement day, string code)
    {
        if (day.TryGetProperty(code, out var el))
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString()?.Replace(",", ""), out var s)) return s;
        }
        return 0m;
    }

    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        value = default;
        if (obj.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = el.GetBoolean();
            return true;
        }
        return false;
    }

    private static bool TryGetInt(JsonElement obj, string name, out int value)
    {
        value = default;
        return obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        value = "";
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
        {
            value = el.GetString() ?? "";
            return true;
        }
        return false;
    }
}
