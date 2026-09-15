using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockWidget.App.Services;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.App.ViewModels;

public enum AddStockResult
{
    Ok,
    InvalidCode,
    AlreadyExists,
    FetchFailed,
    Failed,
}

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IWatchlistRepository _watchlistRepo;
    private readonly IPriceRefreshService _refreshService;
    private readonly ITencentQuoteApi _api;
    private readonly IAlertRepository _alertRepo;
    private readonly IQuoteSnapshotRepository _snapshotRepo;
    private readonly ITradingCalendar _tradingCalendar;
    private readonly IKlineArchiver _klineArchiver;

    private readonly DispatcherTimer _refreshTimer;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _refreshing;
    private AppSettings _cfg = new();
    private List<StockItem> _watchlist = [];

    /// <summary>休市探测间隔（毫秒）：市场关闭时每个 tick 拉长到此，减少无效请求。</summary>
    private static readonly TimeSpan MarketProbeInterval = TimeSpan.FromSeconds(600);

    /// <summary>分组显示顺序（指数 / ETF / 个股 / 港股 / 美股）。</summary>
    private static readonly string[] CategoryOrder = ["指数", "ETF", "个股", "港股", "美股"];

    public ObservableCollection<StockRowViewModel> Rows { get; } = [];

    // ---------------------------
    // 量能栏与状态（旧版底部成交额栏）
    // ---------------------------

    /// <summary>今日沪深两市总成交额（亿）。</summary>
    [ObservableProperty] private string _totalAmountText = "--.--";

    /// <summary>较上一交易日增量 / 缩量描述。</summary>
    [ObservableProperty] private string _amountDiffText = "";

    [ObservableProperty] private string _amountDiffTone = "plain";

    /// <summary>昨日成交额摘要（悬停提示用）。</summary>
    [ObservableProperty] private string _yesterdaySummary = "📈 暂无历史成交额数据";

    /// <summary>状态段：刷新间隔 / 锁定 / 最后更新时间。</summary>
    [ObservableProperty] private string _statusText = "正在初始化…";

    /// <summary>休市徽章（周末）。</summary>
    [ObservableProperty] private bool _marketClosed;

    /// <summary>大盘（上证指数 sh000001）当日涨跌幅，用于托盘色点；自选无该指数时为 null。</summary>
    [ObservableProperty] private decimal? _marketMoodPct;

    [ObservableProperty] private double _opacity = 0.8;

    [ObservableProperty] private string _hotkeyDisplay = "ctrl+q";

    /// <summary>当前选中的行（窗口侧维护）。</summary>
    public StockRowViewModel? SelectedRow { get; set; }

    // ---------------------------
    // 视图事件（窗口侧订阅）
    // ---------------------------

    /// <summary>字段 / 列配置变化，窗口需重建列。</summary>
    public event Action? ColumnsChanged;

    /// <summary>请求打开设置中心。</summary>
    public event Action? SettingsRequested;

    /// <summary>请求打开关于窗口。</summary>
    public event Action? AboutRequested;

    /// <summary>请求退出程序。</summary>
    public event Action? QuitRequested;

    /// <summary>热键配置变化，窗口需重注册。</summary>
    public event Action<string>? HotkeyChanged;

    /// <summary>预警触发（App 负责弹通知）。</summary>
    public event Action<IReadOnlyList<AlertTrigger>>? AlertsTriggered;

    /// <summary>行双击 → 打开分时图。</summary>
    public event Action<StockRowViewModel>? MinuteRequested;

    /// <summary>窗口显隐切换请求（热键 / 双击表头 / 托盘）。</summary>
    public event Action? VisibilityToggleRequested;

    public MainViewModel(
        ISettingsService settingsService,
        IWatchlistRepository watchlistRepo,
        IPriceRefreshService refreshService,
        ITencentQuoteApi api,
        IAlertRepository alertRepo,
        IQuoteSnapshotRepository snapshotRepo,
        ITradingCalendar tradingCalendar,
        IKlineArchiver klineArchiver)
    {
        _settingsService = settingsService;
        _watchlistRepo = watchlistRepo;
        _refreshService = refreshService;
        _api = api;
        _alertRepo = alertRepo;
        _snapshotRepo = snapshotRepo;
        _tradingCalendar = tradingCalendar;
        _klineArchiver = klineArchiver;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5000) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public AppSettings Settings => _cfg;

    public ICollectionView RowsView { get; private set; } = null!;

    /// <summary>启动：加载设置与自选股、应用配置、首刷。导入数据后可重复调用以重载。</summary>
    public async Task InitializeAsync()
    {
        Rows.Clear();
        _cfg = _settingsService.Current.Clone();
        _watchlist = _watchlistRepo.GetAllOrSeed();

        foreach (var item in _watchlist)
        {
            var row = new StockRowViewModel(item);
            Rows.Add(row);
            row.NotifyAllCells();
        }

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        ApplyViewStructure();

        ApplySettingsInternal(save: false);
        await RefreshAsync();
    }

    /// <summary>设置中心"应用 / 保存"后调用。</summary>
    public void ApplySettings(AppSettings cfg)
    {
        _cfg = cfg.Clone();
        ApplySettingsInternal(save: true);
    }

    private void ApplySettingsInternal(bool save)
    {
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1000, _cfg.RefreshIntervalMs));
        ThemeManager.Instance.Apply(_cfg.Theme);
        ThemeManager.Instance.ApplyAccent(_cfg.AccentColor);
        // 透明度：仅背景 alpha 透桌面，文字/数据保持不透明（整窗 Opacity 会把文字一起变透明）
        // 必须在主题字典加载之后调用（基于新主题的原始刷克隆）
        ThemeManager.Instance.ApplyOpacity(_cfg.OpacityPercent);

        try
        {
            AutoStartService.SetEnabled(_cfg.AutoStart);
        }
        catch
        {
            // 注册表写失败不阻断
        }

        ApplyViewStructure();
        ColumnsChanged?.Invoke();
        HotkeyChanged?.Invoke(_cfg.Hotkey);
        HotkeyDisplay = HotkeyParser.NormalizeDisplay(_cfg.Hotkey);
        UpdateStatusText();

        if (save)
            _settingsService.Save(_cfg);
    }

    // ---------------------------
    // 排序与分组
    // ---------------------------

    private void ApplyViewStructure()
    {
        if (RowsView is null) return;

        RowsView.SortDescriptions.Clear();
        if (RowsView is ListCollectionView list)
            list.CustomSort = new RowComparer(_cfg.SortField, _cfg.SortDescending, _cfg.GroupByCategory);
        else
            RowsView.Refresh();
    }

    /// <summary>行排序比较器：分组优先，再按排序字段或自选顺序。</summary>
    private sealed class RowComparer(string? sortField, bool descending, bool groupByCategory) : IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not StockRowViewModel a || y is not StockRowViewModel b) return 0;

            // 分类聚集仅在开启自动分组时生效；关闭后完全按自选手动顺序
            if (groupByCategory)
            {
                var groupA = CategoryOrder.IndexOf(a.CategoryName);
                var groupB = CategoryOrder.IndexOf(b.CategoryName);
                if (groupA != groupB) return groupA.CompareTo(groupB);
            }

            if (sortField is null)
                return a.Info.SortOrder.CompareTo(b.Info.SortOrder);

            var va = a.GetSortValue(sortField);
            var vb = b.GetSortValue(sortField);
            if (va is null && vb is null) return a.Info.SortOrder.CompareTo(b.Info.SortOrder);
            if (va is null) return 1;
            if (vb is null) return -1;

            var cmp = decimal.Compare(va.Value, vb.Value);
            return descending ? -cmp : cmp;
        }
    }

    /// <summary>列头排序请求：点击循环 无排序 → 升序 → 降序 → 无排序。</summary>
    public void SetSort(string? fieldKey)
    {
        if (fieldKey is null) return;

        if (_cfg.SortField != fieldKey)
        {
            _cfg.SortField = fieldKey;
            _cfg.SortDescending = false;
        }
        else if (!_cfg.SortDescending)
        {
            _cfg.SortDescending = true;
        }
        else
        {
            _cfg.SortField = null; // 取消排序
        }

        _settingsService.Save(_cfg);
        ApplyViewStructure();
    }

    // ---------------------------
    // 刷新
    // ---------------------------

    public void StartRefreshLoop() => _refreshTimer.Start();

    public void StopRefreshLoop() => _refreshTimer.Stop();

    [RelayCommand]
    private async Task RefreshNowAsync()
    {
        StatusText = "刷新中……";
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return; // 防重入（与旧版语义一致）
        _refreshing = true;
        try
        {
            // 休市（节假日/周末/非交易日）：跳过抓取，临时拉长到探测间隔，恢复后切回配置间隔
            if (IsMarketClosed(DateTime.Now))
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    MarketClosed = true;
                    UpdateStatusText();
                    if (_refreshTimer.Interval != MarketProbeInterval)
                        _refreshTimer.Interval = MarketProbeInterval;
                });
                return;
            }
            var prev = Rows.Select(r => r.Current).ToList();
            var result = await Task.Run(async () => await _refreshService
                .RefreshAsync(_watchlist, prev)
                .ConfigureAwait(false));

            await _dispatcher.InvokeAsync(() =>
            {
                foreach (var row in Rows)
                {
                    var fresh = result.Quotes.FirstOrDefault(q =>
                        string.Equals(q.Code, row.Code, StringComparison.OrdinalIgnoreCase));
                    fresh ??= QuoteData.Failed(row.Code);
                    row.Update(fresh);
                }

                MarketClosed = IsMarketClosed(DateTime.Now);
                // 进入交易日：恢复配置的刷新间隔
                var cfgInterval = TimeSpan.FromMilliseconds(_cfg.RefreshIntervalMs);
                if (_refreshTimer.Interval != cfgInterval)
                    _refreshTimer.Interval = cfgInterval;
                UpdateAmountBar(result);
                // 大盘涨跌 → 托盘色点（仅在自选含上证指数时更新；失败保留旧值不闪烁）
                var mood = result.Quotes.FirstOrDefault(q =>
                    string.Equals(q.Code, MainIndexCodes.Shanghai, StringComparison.OrdinalIgnoreCase));
                MarketMoodPct = mood?.Success == true ? mood.ChangePct : MarketMoodPct;
                UpdateStatusText();
                if (_cfg.ShowSparkline)
                    LoadSparklines();
            });

            if (result.TriggeredAlerts.Count > 0)
                AlertsTriggered?.Invoke(result.TriggeredAlerts);

            // 收盘归档：交易日 15:05 后首个刷新落库（后台执行避免阻塞 UI；幂等可重试）
            var archivedQuotes = result.Quotes;
            var now = DateTime.Now;
            _ = Task.Run(() => _klineArchiver.TryArchive(archivedQuotes, now));
        }
        catch (Exception)
        {
            // 网络 / 数据异常：状态栏提示，行数据保留旧值（与旧版一致）
            await _dispatcher.InvokeAsync(() =>
            {
                MarketClosed = IsMarketClosed(DateTime.Now);
                UpdateStatusText();
                // 与成功分支一致：进入交易日即恢复配置刷新间隔（即便本轮抓取失败）
                var cfgInt = TimeSpan.FromMilliseconds(_cfg.RefreshIntervalMs);
                if (_refreshTimer.Interval != cfgInt)
                    _refreshTimer.Interval = cfgInt;
            });
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateAmountBar(RefreshResult result)
    {
        if (!_cfg.ShowTotalAmount)
        {
            TotalAmountText = "";
            AmountDiffText = "";
            return;
        }

        TotalAmountText = result.TotalAmountYi is { } total
            ? total.ToString("N2", CultureInfo.InvariantCulture)
            : "--.--";

        YesterdaySummary = result.YesterdayTotalYi is { } yest
            ? $"📈 昨日成交额 {yest:N2} 亿"
            : "📈 暂无历史成交额数据";

        if (result.AmountDiff is { } diff)
        {
            AmountDiffText = diff >= 0
                ? $"（较上一交易日增量 {Math.Abs(Math.Round(diff, 0)):N0} 亿）"
                : $"（较上一交易日缩量 {Math.Abs(Math.Round(diff, 0)):N0} 亿）";
            AmountDiffTone = diff >= 0 ? "up" : "down";
        }
        else
        {
            AmountDiffText = "";
            AmountDiffTone = "plain";
        }
    }

    private string BuildStatusSuffix()
    {
        var parts = new List<string>();
        if (_cfg.ShowRefreshInterval)
            parts.Add($"刷新间隔 {_cfg.RefreshIntervalMs / 1000.0:0.#} 秒");
        if (_cfg.ShowLockedStatus)
            parts.Add(_cfg.Locked ? "窗口已锁定" : "窗口未锁定");

        if (_cfg.ShowUpdateWeekday || _cfg.ShowUpdateWeekNumber)
        {
            var now = DateTime.Now;
            var weekday = now.DayOfWeek switch
            {
                DayOfWeek.Monday => "星期一",
                DayOfWeek.Tuesday => "星期二",
                DayOfWeek.Wednesday => "星期三",
                DayOfWeek.Thursday => "星期四",
                DayOfWeek.Friday => "星期五",
                DayOfWeek.Saturday => "星期六",
                _ => "星期日",
            };
            var timePart = $"最后更新时间：{now:yyyy/MM/dd HH:mm:ss}";
            if (_cfg.ShowUpdateWeekday) timePart += $" {weekday}";
            if (_cfg.ShowUpdateWeekNumber) timePart += $"（第{ISOWeek.GetWeekOfYear(now)}周）";
            parts.Add(timePart);
        }

        return string.Join(" / ", parts);
    }

    private void UpdateStatusText() => StatusText = BuildStatusSuffix();

    /// <summary>休市判断（优先交易日历，失败兜底周末）。</summary>
    private bool IsMarketClosed(DateTime now) => !_tradingCalendar.IsTradingDay(now);

    // ---------------------------
    // 迷你走势（当日快照）
    // ---------------------------

    /// <summary>
    /// 加载当日走势快照。数据库查询全部在后台线程执行——此前在 UI 线程逐行同步查询
    /// （N 只股票 = N 次查询，且与后台快照写入抢锁），每 5 秒刷新一次导致 UI 周期性卡顿，
    /// 表现为透明度/切换主题/调整顺序等操作间歇性无响应。
    /// </summary>
    private void LoadSparklines()
    {
        var today = DateTime.Today;
        var codes = Rows.Select(r => r.Code).ToList();
        var snapshotRepo = _snapshotRepo;
        _ = Task.Run(async () =>
        {
            var map = new Dictionary<string, IReadOnlyList<decimal>>(StringComparer.OrdinalIgnoreCase);
            foreach (var code in codes)
            {
                try
                {
                    map[code] = snapshotRepo.GetTodayByCode(code, today)
                        .Select(s => s.Price ?? 0m)
                        .ToList();
                }
                catch
                {
                    map[code] = [];
                }
            }

            await _dispatcher.InvokeAsync(() =>
            {
                foreach (var row in Rows)
                {
                    if (!map.TryGetValue(row.Code, out var pts)) continue;
                    if (row.SparkValues.Count == pts.Count && row.SparkValues.SequenceEqual(pts)) continue;
                    row.SparkValues = pts;
                }
            });
        });
    }

    /// <summary>取某只股票的分时数据（分时弹窗）。</summary>
    public Task<MinuteLineData?> GetMinuteLineAsync(string code) => _api.FetchMinuteLineAsync(code);

    /// <summary>取某只股票当日快照价格序列（分时数据不可用时的回退）。</summary>
    public List<decimal> GetTodaySnapshots(string code)
    {
        try
        {
            return _snapshotRepo.GetTodayByCode(code, DateTime.Today)
                .Select(s => s.Price ?? 0m)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>取某只股票的昨日收盘价（分时图基线）。</summary>
    public decimal? GetPrevClose(string code)
    {
        try
        {
            var row = Rows.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            return row?.Current.PrevClose;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------
    // 自选股操作（右键菜单）
    // ---------------------------

    public async Task<AddStockResult> AddStockAsync(string rawCode)
    {
        var code = StockCodeNormalizer.Normalize(rawCode);
        if (code.Length == 0) return AddStockResult.InvalidCode;
        if (_watchlist.Any(w => w.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return AddStockResult.AlreadyExists;

        // 抓一次验证可取到数据（与旧版一致）
        var probe = await _api.FetchQuotesAsync([code]);
        var info = probe.FirstOrDefault();
        if (info is not { Success: true })
            return AddStockResult.FetchFailed;

        if (!_watchlistRepo.Add(code, info.Name))
            return AddStockResult.AlreadyExists;

        _watchlist = _watchlistRepo.GetAll();
        var row = new StockRowViewModel(_watchlist.First(w => w.Code == code), info);
        Rows.Add(row);
        row.NotifyAllCells();
        ApplyViewStructure();
        return AddStockResult.Ok;
    }

    /// <summary>删除选中股票（固定股不可删）。返回 false 表示未删除。</summary>
    public bool RemoveSelected()
    {
        var row = SelectedRow;
        if (row is null) return false;
        if (!_watchlistRepo.Remove(row.Code)) return false;

        _watchlist = _watchlistRepo.GetAll();
        var target = Rows.FirstOrDefault(r => r.Code == row.Code);
        if (target is not null) Rows.Remove(target);
        SelectedRow = null;
        return true;
    }

    /// <summary>调整顺序：0 置顶 / -1 上移 / 1 下移 / 2 置底（固定股不可移动）。</summary>
    public bool MoveSelected(int direction)
    {
        var row = SelectedRow;
        if (row is null) return false;

        var ordered = _watchlistRepo.GetAll();
        var idx = ordered.FindIndex(w => w.Code == row.Code);
        if (idx < 0 || ordered[idx].IsPinned) return false;

        var target = direction switch
        {
            // 移至顶部：落到第一个非固定股位置（固定股恒居首，否则目标位被占永远失败）
            0 => ordered.FindIndex(w => !w.IsPinned),
            2 => ordered.Count - 1,
            -1 => idx - 1,
            1 => idx + 1,
            _ => idx,
        };
        if (target < 0 || target >= ordered.Count || target == idx) return false;

        App.WriteCrashLog("Diag", new Exception($"MoveSelected 进入: code={row.Code} dir={direction} pinned={ordered[idx].IsPinned}"));
        if (!_watchlistRepo.Move(row.Code, target)) 
        {
            App.WriteCrashLog("Diag", new Exception("Repo.Move 返回 false"));
            return false;
        }
        App.WriteCrashLog("Diag", new Exception($"Move {row.Code} -> {target} 成功，开始同步视图"));

        _watchlist = _watchlistRepo.GetAll();
        // 同步行 VM 持有的条目（保持 Info 与库内一致）
        var itemsByCode = _watchlist.ToDictionary(w => w.Code, StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rows)
            if (itemsByCode.TryGetValue(r.Code, out var item)) r.UpdateItem(item);
        // 手动调整顺序 = 退出排序模式
        if (_cfg.SortField is not null)
        {
            _cfg.SortField = null;
            _cfg.SortDescending = false;
            _settingsService.Save(_cfg);
        }
        // 视图切回"集合顺序"模式，并按新顺序物理移动行——立即生效，不依赖 CustomSort 刷新时机
        if (RowsView is ListCollectionView lcv)
            lcv.CustomSort = null;
        for (var i = 0; i < _watchlist.Count && i < Rows.Count; i++)
        {
            var rowVm = Rows.FirstOrDefault(r => r.Code == _watchlist[i].Code);
            if (rowVm is not null)
            {
                var cur = Rows.IndexOf(rowVm);
                if (cur != i) Rows.Move(cur, i);
            }
        }
        return true;
    }

    // ---------------------------
    // 请求转发（窗口 / 右键菜单）
    // ---------------------------

    public void RequestSettings() => SettingsRequested?.Invoke();

    public void RequestAbout() => AboutRequested?.Invoke();

    public void RequestQuit() => QuitRequested?.Invoke();

    public void RequestVisibilityToggle() => VisibilityToggleRequested?.Invoke();

    public void RequestMinute(StockRowViewModel row) => MinuteRequested?.Invoke(row);

    [RelayCommand]
    private void RefreshStatusText() => UpdateStatusText();

    /// <summary>锁定状态切换（右键菜单 / 设置）。</summary>
    public bool ToggleLock()
    {
        _cfg.Locked = !_cfg.Locked;
        _settingsService.Save(_cfg);
        UpdateStatusText();
        return _cfg.Locked;
    }

    /// <summary>主题切换：深色 ↔ 浅色直接互换（每次点击必然变化，不再经过 system 出现"看起来没切"）。</summary>
    public string ToggleTheme()
    {
        _cfg.Theme = _cfg.Theme is "dark" ? "light" : "dark";
        _settingsService.Save(_cfg);
        ThemeManager.Instance.Apply(_cfg.Theme);
        return _cfg.Theme;
    }

    /// <summary>窗口位置持久化（拖动结束时调用）。</summary>
    public void SaveWindowPosition(double x, double y)
    {
        _cfg.WindowX = (int)x;
        _cfg.WindowY = (int)y;
        _settingsService.Save(_cfg);
    }

    /// <summary>退出前保存。</summary>
    public void Shutdown()
    {
        StopRefreshLoop();
        _settingsService.Save(_cfg);
    }
}
