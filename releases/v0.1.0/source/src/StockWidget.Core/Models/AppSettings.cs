namespace StockWidget.Core.Models;

/// <summary>
/// 全部应用设置（强类型）。整体序列化为 JSON 存入 settings 表（key = "app"）。
/// 字段与旧版 stock_config.json 对齐，新增项见尾部；默认值除注明外均与旧版一致。
/// </summary>
public sealed class AppSettings
{
    // ---------- 窗口 ----------
    public bool Locked { get; set; } = false;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;

    /// <summary>窗口不透明度百分比（10–100），旧版为 1–10 的整数，导入时 ×10。</summary>
    public int OpacityPercent { get; set; } = 80;

    // ---------- 外观 ----------
    /// <summary>dark / light / system。</summary>
    public string Theme { get; set; } = "dark";

    public string FontFamily { get; set; } = "微软雅黑";
    public int FontSize { get; set; } = 10;
    public bool FontItalic { get; set; } = true;

    /// <summary>显示行间分隔线（旧版定义了配置但未实现，本版落实）。</summary>
    public bool ShowDividers { get; set; } = false;

    // ---------- 刷新与显示 ----------
    /// <summary>刷新间隔（毫秒）。</summary>
    public int RefreshIntervalMs { get; set; } = 5000;

    public bool ShowTotalAmount { get; set; } = true;
    public bool ShowRefreshInterval { get; set; } = true;
    public bool ShowLockedStatus { get; set; } = true;
    public bool ShowUpdateWeekday { get; set; } = true;

    /// <summary>旧版 default_config 为 True、运行时缺省为 False，此处统一为 False（与用户实际配置一致）。</summary>
    public bool ShowUpdateWeekNumber { get; set; } = false;

    // ---------- 字段 ----------
    /// <summary>额外显示的字段 key 列表（必选字段 name/price/change/amplitude 恒显示）。</summary>
    public List<string> CustomFields { get; set; } = ["high", "low", "open", "prev_close"];

    /// <summary>各字段列宽（字符单位），key 为字段 key。</summary>
    public Dictionary<string, int> FieldWidths { get; set; } = [];

    // ---------- 热键 ----------
    /// <summary>全局显隐热键，如 "ctrl+q"。</summary>
    public string Hotkey { get; set; } = "ctrl+q";

    // ---------- 增强：桌面体验包 ----------
    public bool AutoStart { get; set; } = false;

    /// <summary>涨跌幅预警总开关。</summary>
    public bool AlertsEnabled { get; set; } = true;

    // ---------- 增强：表格增强包 ----------
    /// <summary>按指数 / ETF / 个股自动分组。</summary>
    public bool GroupByCategory { get; set; } = true;

    /// <summary>排序列的字段 key；null 表示按自选股顺序。</summary>
    public string? SortField { get; set; }

    /// <summary>true = 降序。</summary>
    public bool SortDescending { get; set; } = false;

    // ---------- 增强：迷你走势图 ----------
    public bool ShowSparkline { get; set; } = true;

    /// <summary>克隆（设置对话框取消恢复用）。</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
