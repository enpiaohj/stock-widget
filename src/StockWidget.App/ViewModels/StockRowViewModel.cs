using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StockWidget.Core.Models;

namespace StockWidget.App.ViewModels;

/// <summary>单元格值：文本 + 色调（up=红涨 / down=绿跌 / flat / fail / plain）。</summary>
public sealed record CellValue(string Text, string Tone);

/// <summary>
/// 行情行。列渲染通过字符串索引器取 <see cref="CellValue"/>，
/// 更新后以 OnPropertyChanged("Item[]") 通知全部列刷新。
/// </summary>
public partial class StockRowViewModel : ObservableObject
{
    private static readonly string[] PercentFieldKeys = ["change", "turnover", "amplitude"];
    private static readonly string[] CountFieldKeys = ["volume", "amount"];

    private readonly DispatcherTimer _arrowTimer;

    /// <summary>自选股元信息。</summary>
    public StockItem Info { get; private set; }

    /// <summary>当前行情（可能是失败占位）。</summary>
    public QuoteData Current { get; private set; }

    public string Code => Info.Code;

    public string CodeDisplay => StockCodeNormalizer.StripPrefix(Info.Code);

    /// <summary>行闪烁背景（值变动时短暂着色，600ms 后恢复）。</summary>
    [ObservableProperty]
    private Brush? _flashBrush = Brushes.Transparent;

    /// <summary>分时迷你走势数据（启用该列时由主 VM 填充）。</summary>
    [ObservableProperty]
    private IReadOnlyList<decimal> _sparkValues = [];

    public StockRowViewModel(StockItem item, QuoteData? initial = null)
    {
        Info = item;
        Current = initial ?? QuoteData.Failed(item.Code);
        _arrowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _arrowTimer.Tick += (_, _) =>
        {
            _arrowTimer.Stop();
            OnPropertyChanged(PriceIndex);
        };
    }

    /// <summary>替换自选股条目（顺序调整后同步最新 SortOrder）。</summary>
    public void UpdateItem(StockItem item) => Info = item;

    /// <summary>列绑定入口：StockRowViewModel 字符串索引器。</summary>
    public CellValue this[string fieldKey] => BuildCell(fieldKey);

    private const string PriceIndex = "Item[price]";

    /// <summary>通用更新通知：刷新所有索引器绑定。</summary>
    public void NotifyAllCells() => OnPropertyChanged("Item[]");

    /// <summary>分组名（沪深指数 / 沪深个股 / ETF基金 / 香港股票 / 美国股票）。</summary>
    public string CategoryName => Info.Category switch
    {
        StockCategory.Index => "沪深指数",
        StockCategory.Etf => "ETF基金",
        StockCategory.HongKong => "香港股票",
        StockCategory.UsStock => "美国股票",
        _ => "沪深个股",
    };

    /// <summary>按字段 key 取排序值。</summary>
    public decimal? GetSortValue(string fieldKey) =>
        fieldKey == "name" ? null : Current.GetField(fieldKey);

    /// <summary>
    /// 用新行情更新行。与旧值对比驱动：现价 ↑/↓ 箭头（1.2s 恢复）、行背景闪烁（600ms）。
    /// </summary>
    /// <summary>关键显示字段是否与旧值完全一致（一致则跳过全列重算，降低刷新期 UI 负载）。</summary>
    private bool SameAsPrevious(QuoteData a, QuoteData b) =>
        a.Success == b.Success && a.Name == b.Name && a.Price == b.Price && a.ChangePct == b.ChangePct
        && a.Volume == b.Volume && a.Amount == b.Amount && a.Turnover == b.Turnover
        && a.High == b.High && a.Low == b.Low && a.Open == b.Open && a.PrevClose == b.PrevClose
        && a.MarketCap == b.MarketCap && a.Amplitude == b.Amplitude;

    public void Update(QuoteData fresh, bool enableEffects = true)
    {
        var old = Current;
        Current = fresh;

        // 数据未变化（非交易时段轮询/节假日后等）：跳过全列通知与动效，避免无谓重绘
        if (SameAsPrevious(old, fresh))
            return;

        if (enableEffects && fresh.Success && old.Success
            && fresh.Price is { } np && old.Price is { } op && np != op)
        {
            var up = np > op;
            FlashBrush = Application.Current.Resources[up ? "FlashUpBrush" : "FlashDownBrush"] as Brush;
            _ = ResetFlashAsync();
            _arrowTimer.Stop();
            _arrowTimer.Start();
        }
        else if (FlashBrush != Brushes.Transparent)
        {
            FlashBrush = Brushes.Transparent;
        }

        NotifyAllCells();
    }

    private async Task ResetFlashAsync()
    {
        await Task.Delay(600).ConfigureAwait(true);
        FlashBrush = Brushes.Transparent;
    }

    /// <summary>整行色调：按涨跌幅红 / 绿（对齐旧版"整行同色"），平盘 / 无数据用默认色。</summary>
    private string RowTone => Current.ChangePct switch
    {
        null or 0 => "flat",
        > 0 => "up",
        _ => "down",
    };

    private CellValue BuildCell(string fieldKey)
    {
        if (!Current.Success)
        {
            // 首刷成功前不渲染占位符：仅名称列显示自选名，其余列留白等真实行情填充
            return fieldKey == "name"
                ? new CellValue(Info.Name, "fail")
                : new CellValue("", "plain");
        }

        var rowTone = RowTone;

        switch (fieldKey)
        {
            case "name":
            {
                var name = Current.Name is "-" or "" ? (Info.Name.Length > 0 ? Info.Name : "-") : Current.Name;
                return new CellValue(name, rowTone);
            }
            case "price":
            {
                // 箭头期间（timer 运行中）显示 ↑/↓
                if (_arrowTimer.IsEnabled && Current.Price is { } ap)
                {
                    var upNow = Current.ChangePct is { } cp ? cp > 0 : ap >= Current.PrevClose;
                    return new CellValue($"{ap:0.###} {(upNow ? "↑" : "↓")}", upNow ? "up" : "down");
                }
                return Current.Price is { } p ? new CellValue($"{p:0.###}", rowTone) : new CellValue("-", "plain");
            }
        }

        var v = Current.GetField(fieldKey);
        if (v is null) return new CellValue("-", "plain");

        var text = PercentFieldKeys.Contains(fieldKey) ? $"{v:0.00}%"
            : CountFieldKeys.Contains(fieldKey) ? $"{v:N0}"
            : $"{v:0.###}";
        return new CellValue(text, rowTone);
    }
}
