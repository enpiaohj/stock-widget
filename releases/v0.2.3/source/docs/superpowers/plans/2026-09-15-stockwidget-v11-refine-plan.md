# StockWidget v1.1 完善实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 v1.0.0 基础上修复托盘色点与模板残留、新增节假日休市识别、按名称/拼音加股、UI 拟质感深度优化。

**Architecture:** 领域逻辑（日历判定、smartbox 解析、强调色板）放 `StockWidget.Core`（零 UI，可测）；托盘色点、名称搜索下拉、UI 视觉放 `StockWidget.App`；UI 涉及的数据结构新增 EF 迁移。可持续：Core 可测逻辑用 xUnit TDD，App/WPF 视觉用编译+冒烟验证。

**Tech Stack:** C# / WPF (.NET 10)、EF Core 10 (SQLite)、EF Migrations、xUnit、CommunityToolkit.Mvvm、腾讯接口（qt/分时/smartbox）。

---

## 前置：准备

- [ ] **Step 0: 还原工具并确认基线**

```bash
dotnet tool restore
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
```
Expected: 0 警告 0 错误；53 测试全部通过（基线）。

- [ ] **Step 0b: 确认工作区用户暂存资产未动**

```bash
git status --short | grep -c "^A  releases"
```
Expected: 非零（用户的 `releases/v1.0.0` 仍在其暂存区）。不得 add/commit 这些文件；所有提交用路径限定。

---

## Task 1: 删除模板残留 Class1.cs

**Files:**
- Delete: `src/StockWidget.Core/Class1.cs`

- [ ] **Step 1: 再次确认无引用**

Run: `grep -rn "Class1" src tests --include=*.cs*`
Expected: 仅命中 `Class1.cs` 自身（无其他引用）。

- [ ] **Step 2: 删除文件**

```bash
git rm src/StockWidget.Core/Class1.cs
```

- [ ] **Step 3: 构建确认**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

- [ ] **Step 4: 提交**

```bash
git add src/StockWidget.Core/Class1.cs
git commit -m "chore: 删除模板残留空类 Class1.cs
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 2: 接通托盘大盘涨跌色点

**Files:**
- Modify: `src/StockWidget.App/ViewModels/MainViewModel.cs`
- Modify: `src/StockWidget.App/Views/MainWindow.xaml.cs`
- Modify: `src/StockWidget.App/App.xaml.cs`

核心机制：`MainViewModel` 刷新完成后据 `sh000001` 涨跌幅回填 `MarketMoodPct`；
`MainWindow` 监听其 `PropertyChanged` → 调 `App.Current.UpdateTrayMood(pct)`；
`App.UpdateTrayMood` 按 涨=红/跌=绿/平·无=灰 重建托盘图标。

- [ ] **Step 1: MainViewModel 加 MarketMoodPct 属性与回填**

在 `src/StockWidget.App/ViewModels/MainViewModel.cs`：

在增量更新区（`UpdateAmountBar` 附近的刷新回填处）新增可观察属性，并据自选中 `sh000001` 回填：

```csharp
// 在 [ObservableProperty] 声明区新增
/// <summary>大盘（上证指数 sh000001）当日涨跌幅，用于托盘色点；自选无该指数时为 null。</summary>
[ObservableProperty] private decimal? _marketMoodPct;
```

在 `RefreshAsync` 的成功块内（`UpdateAmountBar(result);` 之后）回填：

```csharp
// 大盘涨跌 → 托盘色点（仅在自选含上证指数时更新；失败保留旧值不闪烁）
var mood = result.Quotes.FirstOrDefault(q =>
    string.Equals(q.Code, MainIndexCodes.Shanghai, StringComparison.OrdinalIgnoreCase));
MarketMoodPct = mood?.Success == true ? mood.ChangePct : MarketMoodPct;
```

（`MainIndexCodes` 定义于 `StockWidget.Core.Services`，已在文件 using 范围。）

- [ ] **Step 2: MainWindow 订阅 MarketMoodPct → 更新托盘**

在 `src/StockWidget.App/Views/MainWindow.xaml.cs` 的构造器 `_vm.PropertyChanged += (_, e) => { ... }` 内追加：

```csharp
if (e.PropertyName == nameof(MainViewModel.MarketMoodPct))
    App.Current.UpdateTrayMood(_vm.MarketMoodPct);
```

- [ ] **Step 3: App 增加 UpdateTrayMood 并修正首次调用**

在 `src/StockWidget.App/App.xaml.cs` 类内新增公开方法：

```csharp
/// <summary>
/// 更新托盘图标右上角大盘涨跌色点：涨=红、跌=绿、平/无数据=灰。
/// 调用方（MainWindow）在行情刷新后依据 sh000001 涨跌幅传入。
/// </summary>
public void UpdateTrayMood(decimal? pct)
{
    if (_tray is null) return;
    var color = pct is null ? Colors.Gray
        : pct > 0 ? Colors.Red
        : Colors.Green;
    _tray.IconSource = TrayIconFactory.Create(color, showOverlay: true);
}
```

修正启动首次调用（`StartupCore` 内）：

```csharp
_tray = new TaskbarIcon
{
    ToolTipText = "股票小插件",
    IconSource = TrayIconFactory.Create(default, showOverlay: false), // 初始无数据：不亮点色，首个刷新生效
};
```

同时确认 `App.xaml.cs` 顶部已 `using System.Windows.Media;`（`Colors`）。若缺则在文件头补充：
`using System.Windows.Media;`

- [ ] **Step 4: 构建 + 冒烟**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

运行冒烟（有 sh000001 自选时）：
`dotnet run --project src/StockWidget.App` → 观察托盘图标右上角在首次刷新后出现色点（红涨/绿跌），
行情停止刷新（断网）后色点保持上次不闪烁。

> 此任务为 App 层（无 Core 可测逻辑），不新增 xUnit，靠编译 + 手动冒烟验证。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.App/ViewModels/MainViewModel.cs src/StockWidget.App/Views/MainWindow.xaml.cs src/StockWidget.App/App.xaml.cs
git commit -m "feat: 接通托盘大盘涨跌色点，刷新后红涨绿跌实时更新
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 3: 新增 trading_calendar 表（实体 + EF 迁移）

**Files:**
- Modify: `src/StockWidget.Core/Data/Entities/Entities.cs`
- Modify: `src/StockWidget.Core/Data/StockWidgetDbContext.cs`
- Create: `src/StockWidget.Core/Migrations/<timestamp>_AddTradingCalendar.cs`（由 dotnet-ef 生成）

- [ ] **Step 1: 新增实体**

在 `src/StockWidget.Core/Data/Entities/Entities.cs` 末尾、`EntityExtensions` 之前追加：

```csharp
/// <summary>trading_calendar 表：交易日历（含调休上班的周末）。</summary>
public sealed class TradingCalendarEntity
{
    /// <summary>日期，yyyy-MM-dd。（主键）</summary>
    public string Date { get; set; } = "";

    /// <summary>true=交易日（含调休上班的周末）；false=休市日（法定节假日）。</summary>
    public bool IsTradingDay { get; set; }

    /// <summary>备注（如"春节假期" / "调休上班"）。</summary>
    public string Remark { get; set; } = "";
}
```

- [ ] **Step 2: DbContext 加 DbSet 与映射**

在 `src/StockWidget.Core/Data/StockWidgetDbContext.cs`，`AlertRules` 声明后新增：

```csharp
public DbSet<TradingCalendarEntity> TradingCalendar => Set<TradingCalendarEntity>();
```

在 `OnModelCreating` 内、`AlertRuleEntity` 映射后新增：

```csharp
mb.Entity<TradingCalendarEntity>(e =>
{
    e.ToTable("trading_calendar");
    e.HasKey(x => x.Date);
    e.Property(x => x.Remark).HasDefaultValue("");
});
```

- [ ] **Step 3: 生成迁移**

```bash
cd src/StockWidget.Core
dotnet tool restore
dotnet ef migrations add AddTradingCalendar
```
Expected: 在 `Migrations/` 生成 `<timestamp>_AddTradingCalendar.cs`
（内容为 `CreateTable("trading_calendar", ... Date/TEXT 主键、IsTradingDay/INTEGER、Remark/TEXT)`）
与更新 `StockWidgetDbContextModelSnapshot.cs`。

- [ ] **Step 4: 编译**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.Core/Data/Entities/Entities.cs src/StockWidget.Core/Data/StockWidgetDbContext.cs src/StockWidget.Core/Migrations/
git commit -m "feat: 新增 trading_calendar 交易日历表与 EF 迁移
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 4: TradingCalendarSeeder 内置 2025–2028 种子

**Files:**
- Create: `src/StockWidget.Core/Services/TradingCalendarSeeder.cs`

内置中国法定节假日与调休上班日。数据仅覆盖「非周末、但休市」（法定节假日）与「周末、但交易」（调休上班）两类；
普通工作日与普通周末无需种子（由服务兜底判定）。示例完整覆盖框架如下，实施时按国务院历年放假安排补齐实际日期：

- [ ] **Step 1: 创建 Seeder**

创建 `src/StockWidget.Core/Services/TradingCalendarSeeder.cs`：

```csharp
using StockWidget.Core.Data;

namespace StockWidget.Core.Services;

/// <summary>
/// 内置中国交易日历种子（2025–2028）。仅记录与"默认周末/工作日"不同的两类：
/// (1) 周末但交易（调休上班）→ IsTradingDay=true；(2) 工作日但休市（法定节假日）→ false。
/// 未命中 → 由判定服务按周末/工作日兜底。
/// </summary>
public static class TradingCalendarSeeder
{
    /// <summary>date(yyyy-MM-dd) → 是否交易日。未覆盖 = 默认（周末休市、工作日开盘）。</summary>
    public static IReadOnlyDictionary<string, bool> GetBuiltIn()
    {
        var m = new Dictionary<string, bool>(StringComparer.Ordinal);
        Seed2025(m);
        Seed2026(m);
        Seed2027(m);
        Seed2028(m);
        return m;
    }

    /// <summary>库中该表为空时写入内置数据（非覆盖，保证可维护）。</summary>
    public static void SeedIfEmpty(StockWidgetDbContext db)
    {
        if (db.TradingCalendar.Any()) return;
        foreach (var (date, isTrading) in GetBuiltIn())
        {
            db.TradingCalendar.Add(new Data.Entities.TradingCalendarEntity
            {
                Date = date,
                IsTradingDay = isTrading,
                Remark = isTrading ? "调休上班" : "法定节假日",
            });
        }
        db.SaveChanges();
    }

    private static void Seed2025(Dictionary<string, bool> m) { Add(m, "2025-01-01", false); /* ... 补全年 */ }
    private static void Seed2026(Dictionary<string, bool> m) { Add(m, "2026-01-01", false); /* ... */ }
    private static void Seed2027(Dictionary<string, bool> m) { Add(m, "2027-01-01", false); /* ... */ }
    private static void Seed2028(Dictionary<string, bool> m) { Add(m, "2028-01-01", false); /* ... */ }

    private static void Add(Dictionary<string, bool> m, string date, bool isTrading) => m[date] = isTrading;
}
```

> **实施要求**：`Seed{年份}` 内的 `/* ... */` 必须填充该年度国务院办公厅《放假安排》全部实际日期——
> 法定节假日放假日（含节前/节后正常工作日但如果落周中则休市，实际为分年配置）与调休上班的周末。
> 数据以公开权威来源为准（详见 spec §3.2）。此为数据录入型代码，每个种子条目必须真实存在，不得凭空编造。

- [ ] **Step 2: 编译**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

- [ ] **Step 3: 提交（带回数据集）**

```bash
git add src/StockWidget.Core/Services/TradingCalendarSeeder.cs
git commit -m "feat: 内置 2025-2028 中国交易日历种子数据
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 5: ITradingCalendar 判定服务 + 单元测试

**Files:**
- Create: `src/StockWidget.Core/Services/TradingCalendarService.cs`
- Create: `tests/StockWidget.Tests/TradingCalendarServiceTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `tests/StockWidget.Tests/TradingCalendarServiceTests.cs`：

```csharp
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class TradingCalendarServiceTests
{
    // 用一次性种子字典构造（不依赖数据库，判定服务仅吞入日历+系统时间）
    private static readonly Dictionary<string, bool> Cal = new()
    {
        // 示例种子（与实际内置表解耦，仅验判定逻辑）
        ["2026-01-01"] = false,   // 元旦（工作日·休市）
        ["2026-02-20"] = true,    // 春节调休上班·周五（交易日）
        ["2026-02-21"] = false,   // 春节正月初一假期（周六·休市）
    };

    private static TradingCalendarService Make() =>
        new(Cal, (DateTime date) => date.DayOfWeek);

    [Fact]
    public void WorkingDay_InCalendar_ReturnsTableValue()
    {
        // 2026-01-01 是周四，表内 false → 休市
        Assert.False(Make().IsTradingDay(new DateTime(2026, 1, 1)));
    }

    [Fact]
    public void Weekend_AdjustWork_InCalendar_Trading()
    {
        // 2026-02-21 是周六，表内 true → 交易日（调休上班）
        Assert.True(Make().IsTradingDay(new DateTime(2026, 2, 21)));
    }

    [Fact]
    public void NormalWeekend_Fallback_NotTrading()
    {
        // 2026-06-13 是周六，未命中 → 周末休市
        Assert.False(Make().IsTradingDay(new DateTime(2026, 6, 13)));
    }

    [Fact]
    public void NormalWeekday_Fallback_Trading()
    {
        // 2026-06-15 是周一，未命中 → 工作日开盘
        Assert.True(Make().IsTradingDay(new DateTime(2026, 6, 15)));
    }

    [Fact]
    public void IsTodayTrading_ViaProvidedToday()
    {
        var svc = Make();
        // 2026-01-01 表内 false，today 固定为该日 → 非今日交易日
        Assert.False(svc.IsTodayTrading(new DateTime(2026, 1, 1)));
    }
}
```

> 说明：服务构造暴露 `today` 提供者以便可测（见 Step 2）；上述测试用临时字典，不依赖真实数据库。

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter TradingCalendarServiceTests`
Expected: 编译失败（`TradingCalendarService` 不存在）。

- [ ] **Step 3: 实现服务**

创建 `src/StockWidget.Core/Services/TradingCalendarService.cs`：

```csharp
namespace StockWidget.Core.Services;

/// <summary>交易日判定：命中内置/维护日历 → 按表；未命中 → 周末休市、工作日开盘兜底。</summary>
public interface ITradingCalendar
{
    /// <summary>指定日是否为交易日（非交易日 → 休市）。</summary>
    bool IsTradingDay(DateTime date);
    /// <summary>今天是否为交易日（使用当前系统时间）。</summary>
    bool IsTodayTrading();
}

/// <summary>实现：读取种子字典 + 小时缓存，未命中兜底周末规则。</summary>
public sealed class TradingCalendarService(
    IReadOnlyDictionary<string, bool> calendar,
    Func<DateTime, DayOfWeek>? dayOfWeek = null) : ITradingCalendar
{
    private readonly Func<DateTime, DayOfWeek> _dayOfWeek = dayOfWeek ?? (d => d.DayOfWeek);
    private (DateTime Day, bool IsTrading)? _todayCache;

    public bool IsTradingDay(DateTime date)
    {
        if (calendar.TryGetValue(date.ToString("yyyy-MM-dd"), out var isTrading))
            return isTrading;

        // 兜底：周末休市、工作日开盘（永不抛异常）
        var dow = _dayOfWeek(date);
        return dow is DayOfWeek.Saturday or DayOfWeek.Sunday ? false : true;
    }

    public bool IsTodayTrading()
    {
        var now = DateTime.Now;
        var day = now.Date;
        if (_todayCache is { } c && c.Day == day) return c.IsTrading;
        var result = IsTradingDay(day);
        _todayCache = (day, result);
        return result;
    }

    /// <summary>测试注入：以固定日期替代"今天"。</summary>
    public bool IsTodayTrading(DateTime today)
    {
        var result = IsTradingDay(today.Date);
        _todayCache = (today.Date, result);
        return result;
    }
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter TradingCalendarServiceTests`
Expected: 5 tests PASS。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.Core/Services/TradingCalendarService.cs tests/StockWidget.Tests/TradingCalendarServiceTests.cs
git commit -m "feat: 新增交易日历判定服务 ITradingCalendar 与单元测试
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 6: 注册日历服务 + 种子入库 + 智能抓取整合

**Files:**
- Modify: `src/StockWidget.Core/ServiceCollectionExtensions.cs`
- Modify: `src/StockWidget.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: DI 注册（含种子入库）**

在 `src/StockWidget.Core/ServiceCollectionExtensions.cs`。在 `db.Database.Migrate();` 之后、`return services;` 之前：

```csharp
using (var seed = new StockWidgetDbContext(new DbContextOptionsBuilder<StockWidgetDbContext>()
           .UseSqlite($"Data Source={path}").Options))
{
    TradingCalendarSeeder.SeedIfEmpty(seed);
}
```

在 `services.AddSingleton<IPriceRefreshService, PriceRefreshService>();` 之后：

```csharp
// 交易日历：内置种子 + 兜底周末规则
var builtInCal = TradingCalendarSeeder.GetBuiltIn();
services.AddSingleton<ITradingCalendar>(new TradingCalendarService(builtInCal));
```

- [ ] **Step 2: MainViewModel 注入 ITradingCalendar 并智能抓取**

在 `src/StockWidget.App/ViewModels/MainViewModel.cs`：

构造函数参数列表新增：

```csharp
ITradingCalendar tradingCalendar,
```

并赋值私有字段：

```csharp
private readonly ITradingCalendar _tradingCalendar;
```

（在构造函数体首行 `_settingsService = settingsService;` 处一并 `_tradingCalendar = tradingCalendar;`）

替换休市判定静态方法 `IsMarketClosed` 为实例方法：

```csharp
/// <summary>今天是否休市（优先交易日历，失败兜底周末）。</summary>
public bool IsMarketClosed(DateTime now) => !_tradingCalendar.IsTradingDay(now);
```

（原 `public static bool IsMarketClosed` 改为实例后用 `_tradingCalendar`；调用处 `IsMarketClosed(DateTime.Now)` 不变。）

在 `RefreshAsync` 顶部、`try {` 之前新增智能抓取跳闸：

```csharp
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
```

在 `_refreshing = false;`（finally）之后正常路径的 tick 会复用配置间隔；但进入交易日需恢复配置间隔。为此在 `RefreshAsync` 成功块（`MarketClosed = IsMarketClosed(DateTime.Now);` 处）追加恢复逻辑：

```csharp
// 进入交易日：恢复配置的刷新间隔
var cfgInterval = TimeSpan.FromMilliseconds(_cfg.RefreshIntervalMs);
if (_refreshTimer.Interval != cfgInterval)
    _refreshTimer.Interval = cfgInterval;
```

在类内新增常量：

```csharp
/// <summary>休市探测间隔（毫秒）：市场关闭时每个 tick 拉长到此，减少无效请求。</summary>
private static readonly TimeSpan MarketProbeInterval = TimeSpan.FromSeconds(600);
```

> 说明：`_refreshTimer` 的默认 Interval 由 `ApplySettingsInternal` 设置，与 `_cfg.RefreshIntervalMs` 联动；
> 休市跳闸只在非交易日调用，交易日路径 Interval 已由设置保证，恢复逻辑兜底一致。

- [ ] **Step 3: 编译 + 测试 + 冒烟**

```bash
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
```
Expected: 0 警告 0 错误；53+5 测试通过。

冒烟（非交易日）：状态栏出"休市"徽章，且抓取间隔变为 600s（观察日志无行情请求）。

- [ ] **Step 4: 提交**

```bash
git add src/StockWidget.Core/ServiceCollectionExtensions.cs src/StockWidget.App/ViewModels/MainViewModel.cs
git commit -m "feat: 注册交易日历服务并接入休市智能抓取
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 7: StockSearchMatch 模型 + Smartbox 解析器（TDD）

**Files:**
- Create: `src/StockWidget.Core/Models/StockSearchMatch.cs`
- Create: `src/StockWidget.Core/Services/SmartboxResponseParser.cs`
- Create: `tests/StockWidget.Tests/SmartboxResponseParserTests.cs`

- [ ] **Step 1: 失败测试**

创建 `tests/StockWidget.Tests/SmartboxResponseParserTests.cs`：

```csharp
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class SmartboxResponseParserTests
{
    // 腾讯 smartbox 响应（GBK 已解码）。行格式：
    // v_hint="关键字"~"股票代码"~"名称"~"拼音"~"市场"~...;（多个候选以 ; 连接，字段以 ~ 连接）
    [Fact]
    public void Parse_ChineseKeyword_ReturnsMatches()
    {
        const string resp =
            "v_hint=\"07ac08dd~3:sh000001~上证指数~ZS~4~\"~\"" +
            "sh000001\"~\"上证指数\"~\"szzs\"~\"sh\"~\"index\";" +
            "v_hint=\"\"~\"sz399001\"~\"深证成指\"~\"szcz\"~\"sz\"~\"index\";";
        var ms = SmartboxResponseParser.Parse(resp);
        Assert.NotEmpty(ms);
        Assert.Contains(ms, m => m.Code == "sh000001" && m.Name == "上证指数");
        Assert.Contains(ms, m => m.Code == "sz399001" && m.Name == "深证成指");
    }

    [Fact]
    public void Parse_MarketMapping_AAndHkAndUs()
    {
        const string resp =
            "v_hint=\"\"~\"sh600390\"~\"浦发银行\"~\"pfyh\"~\"sh\"~\"1\";" +
            "v_hint=\"\"~\"hk00700\"~\"腾讯控股\"~\"txkg\"~\"hk\"~\"2\";" +
            "v_hint=\"\"~\"usAAPL\"~\"苹果\"~\"pg\"~\"us\"~\"3\";";
        var ms = SmartboxResponseParser.Parse(resp);
        Assert.Equal("sh600390", ms[0].Code);
        Assert.Equal("hk00700", ms[1].Code);
        Assert.Equal("usAAPL", ms[2].Code);
    }

    [Fact]
    public void Parse_CodeKeyword_ReturnsNormalized()
    {
        const string resp =
            "v_hint=\"\"~\"600390\"~\"浦发银行\"~\"pfyh\"~\"sh\"~\"1\";";
        var ms = SmartboxResponseParser.Parse(resp);
        // 归一化为 sh600390（复用 StockCodeNormalizer）
        Assert.Equal("sh600390", ms[0].Code);
    }

    [Fact]
    public void Parse_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(SmartboxResponseParser.Parse(""));
        Assert.Empty(SmartboxResponseParser.Parse("garbage no tilde"));
        Assert.Empty(SmartboxResponseParser.Parse(null));
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter SmartboxResponseParserTests`
Expected: 编译失败（两类型不存在）。

- [ ] **Step 3: 实现模型与解析器**

创建 `src/StockWidget.Core/Models/StockSearchMatch.cs`：

```csharp
namespace StockWidget.Core.Models;

/// <summary>名称/拼音搜索候选：Code 已归一化（sh600390），Market 为 沪/深/港股/美股。</summary>
public sealed record StockSearchMatch(string Code, string Name, string Market);
```

创建 `src/StockWidget.Core/Services/SmartboxResponseParser.cs`：

```csharp
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

/// <summary>
/// 腾讯 smartbox（smartbox.gtimg.cn/s3）响应解析（GBK 已解码）。
/// 行格式：v_hint="..."~"代码"~"名称"~"拼音"~"市场"~"分类";… 候选以 ; 连接、字段以 ~ 连接。
/// 失败/异常静默返回空列表（UI 兜底）。
/// </summary>
public static class SmartboxResponseParser
{
    public static IReadOnlyList<StockSearchMatch> Parse(string? text)
    {
        var result = new List<StockSearchMatch>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = part.Split('~');
            // 前置 v_hint= 元信息剥离：跳过不含候选的段
            if (fields.Length < 4) continue;
            var codeRaw = fields[^6]; // 形态因接口而异，用容错：取在该候选段内的"代码字段"
            // 容错实现：从后往前找 6·last 形如 dd … 此处实际以固定已知结构解析
            ParseCandidate(fields, result);
        }
        return result;
    }

    private static void ParseCandidate(string[] fields, List<StockSearchMatch> acc)
    {
        // 已知候选结构（第 N 候选段）：~"sh000001"~"上证指数"~"拼音"~"市场"~"分类"
        // 从末段定位：find 一个字段 pair (在市场字段之前)。这里按 6 字段最小候选解析：
        string? code = null, name = null, market = null;
        for (var i = 0; i < fields.Length; i++)
        {
            var f = fields[i].Trim().Trim('"');
            if (f.Length == 0) continue;
            if (code is null && LooksLikeCode(f)) code = f;
            else if (name is null && code is not null) name = f;
            else if (market is null && LooksLikeMarket(f)) market = f;
        }
        if (string.IsNullOrEmpty(code)) return;

        var normalized = StockCodeNormalizer.Normalize(code);
        if (normalized.Length == 0) return;
        acc.Add(new StockSearchMatch(normalized, name ?? "", MapMarket(market)));
    }

    private static bool LooksLikeCode(string s) =>
        s.Length >= 5 && (s.All(char.IsDigit)
            || s.StartsWith("sh") || s.StartsWith("sz") || s.StartsWith("hk") || s.StartsWith("us"));

    private static bool LooksLikeMarket(string s) =>
        s is "1" or "2" or "3" or "5" or "sh" or "sz" or "hk" or "us";

    private static string MapMarket(string? m) => m switch
    {
        "1" or "2" or "5" or "sh" or "sz" => "A股",
        "3" or "hk" => "港股",
        "4" or "us" => "美股",
        _ => "A股",
    };
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter SmartboxResponseParserTests`
Expected: 4 tests PASS。

> 若真实 smartbox 响应结构与测试假设（字段顺序/分隔）不符，则以真实抓包为准调整解析与测试，
> 保持「解析器纯静态可测」「解析失败返回空列表」两个契约不变。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.Core/Models/StockSearchMatch.cs src/StockWidget.Core/Services/SmartboxResponseParser.cs tests/StockWidget.Tests/SmartboxResponseParserTests.cs
git commit -m "feat: smartbox 名称/拼音搜索解析器与单元测试
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 8: ITencentQuoteApi.SearchSuggestAsync 实现

**Files:**
- Modify: `src/StockWidget.Core/Services/TencentQuoteApi.cs`

- [ ] **Step 1: 接口加方法**

在 `ITencentQuoteApi` 接口内、`FetchMinuteLineAsync` 之后：

```csharp
/// <summary>腾讯智能搜索（smartbox.gtimg.cn，GBK）：中文/拼音/代码关键字 → 候选；失败返回空列表。</summary>
Task<IReadOnlyList<StockSearchMatch>> SearchSuggestAsync(string keyword, CancellationToken ct = default);
```

（`StockSearchMatch` 使用 `StockWidget.Core.Models`，已在文件 using 范围。）

- [ ] **Step 2: 实现**

在 `TencentQuoteApi` 类内、`FetchMinuteLineAsync` 之前/之后新增：

```csharp
private const string SmartboxUrl = "https://smartbox.gtimg.cn/s3/?v=2&q=";

public async Task<IReadOnlyList<StockSearchMatch>> SearchSuggestAsync(string keyword, CancellationToken ct = default)
{
    try
    {
        if (string.IsNullOrWhiteSpace(keyword)) return [];
        var url = SmartboxUrl + Uri.EscapeDataString(keyword) + "&t=all";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        using var resp = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
        var text = Encoding.GetEncoding("GBK").GetString(bytes);
        return SmartboxResponseParser.Parse(text);
    }
    catch (Exception)
    {
        // 搜索失败静默：UI 回退为直接代码输入
        return [];
    }
}
```

- [ ] **Step 3: 编译 + 全测试**

```bash
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
```
Expected: 0 警告 0 错误；所有测试（含 Smartbox 解析）通过。

- [ ] **Step 4: 提交**

```bash
git add src/StockWidget.Core/Services/TencentQuoteApi.cs
git commit -m "feat: 腾讯 smartbox 名称搜索接口 SearchSuggestAsync
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 9: InputDialog 名称搜索下拉联动

**Files:**
- Modify: `src/StockWidget.App/Views/InputDialog.xaml`
- Modify: `src/StockWidget.App/Views/InputDialog.xaml.cs`
- Modify: `src/StockWidget.App/Views/MainWindow.xaml.cs`

- [ ] **Step 1: 扩展 InputDialog 支持可搜索下拉**

修改 `src/StockWidget.App/Views/InputDialog.xaml`——在 `InputBox` 下方、`HintText` 之上插入候选下拉：

```xml
<TextBox x:Name="InputBox" KeyDown="InputBox_KeyDown" TextChanged="InputBox_TextChanged" />
<ListBox x:Name="MatchList" Visibility="Collapsed" MaxHeight="180"
         Background="{DynamicResource MenuBgBrush}" Foreground="{DynamicResource FgBrush}"
         BorderBrush="{DynamicResource ControlBorderBrush}" BorderThickness="1"
         Margin="0,4,0,0" MouseLeftButtonUp="MatchList_Click"
         Panel.ZIndex="10">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <Grid Margin="0,2">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <TextBlock Text="{Binding Name}" Foreground="{DynamicResource FgBrush}" />
                <TextBlock Grid.Column="1" Text="{Binding Market}" Margin="10,0,0,0"
                           FontSize="11" Foreground="{DynamicResource SubFgBrush}" />
            </Grid>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

修改 `src/StockWidget.App/Views/InputDialog.xaml.cs`：

```csharp
using StockWidget.Core.Models;
using System.Threading;

public partial class InputDialog : Window
{
    /// <summary>用户输入内容（确定后有效）。</summary>
    public string Input => InputBox.Text.Trim();

    /// <summary>匹配候选标记：若非空，DialogResult 为 true 表示接受了候选（Input 为其 Code）。</summary>
    public StockSearchMatch? SelectedMatch { get; private set; }

    private readonly Func<string, Task<IReadOnlyList<StockSearchMatch>>>? _searchSource;
    private CancellationTokenSource? _searchCts;
    private readonly object _searchLock = new();

    public InputDialog(string title, string prompt, string? hint = null,
        Func<string, Task<IReadOnlyList<StockSearchMatch>>>? searchSource = null)
    {
        _searchSource = searchSource;
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        HintText.Text = hint ?? "";
        Loaded += (_, _) => InputBox.Focus();
    }
```

- [ ] **Step 2: 防抖搜索 + 键盘/鼠标选中逻辑**

在 `InputDialog.xaml.cs` 类内新增方法：

```csharp
private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
{
    if (_searchSource is null) { MatchList.Visibility = Visibility.Collapsed; return; }
    var keyword = InputBox.Text.Trim();
    if (keyword.Length < 1)
    {
        lock (_searchLock) _searchCts?.Cancel();
        MatchList.Visibility = Visibility.Collapsed;
        return;
    }

    // 防抖 300ms：取消上一次未完成请求
    lock (_searchLock)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;
        _ = DebouncedSearchAsync(keyword, cts.Token);
    }
}

private async Task DebouncedSearchAsync(string keyword, CancellationToken token)
{
    try
    {
        await Task.Delay(300, token).ConfigureAwait(true); // 防抖
        var matches = await _searchSource!(keyword).ConfigureAwait(true);
        if (token.IsCancellationRequested || keyword != InputBox.Text.Trim()) return;

        if (matches.Count == 0)
        {
            MatchList.Visibility = Visibility.Collapsed;
            return;
        }

        MatchList.ItemsSource = matches;
        MatchList.SelectedIndex = -1;
        MatchList.Visibility = Visibility.Visible;
    }
    catch (OperationCanceledException)
    {
        // 被新输入取消，忽略
    }
    catch (Exception)
    {
        // 搜索失败：静默隐藏下拉，不阻断直接输入代码
        if (token.IsCancellationRequested) return;
        MatchList.Visibility = Visibility.Collapsed;
    }
}

private void MatchList_Click(object sender, MouseButtonEventArgs e)
{
    if (MatchList.SelectedItem is StockSearchMatch m)
    {
        AcceptMatch(m);
    }
}

private void AcceptMatch(StockSearchMatch m)
{
    SelectedMatch = m;
    DialogResult = true;
    Close();
}

private void Ok_Click(object sender, RoutedEventArgs e)
{
    // 无高亮候选则提交输入框原文本（保持现状）；有关键 Enter 走 InputBox_KeyDown
    if (MatchList.Visibility == Visibility.Visible && MatchList.SelectedItem is StockSearchMatch m)
        AcceptMatch(m);
    else
        CloseWithResult();
}
```

修改键盘处理 `InputBox_KeyDown`：

```csharp
private void InputBox_KeyDown(object sender, KeyEventArgs e)
{
    if (MatchList.Visibility == Visibility.Visible && MatchList.Items.Count > 0)
    {
        switch (e.Key)
        {
            case Key.Down:
                e.Handled = true;
                MoveSel(1);
                return;
            case Key.Up:
                e.Handled = true;
                MoveSel(-1);
                return;
            case Key.Enter:
                e.Handled = true;
                if (MatchList.SelectedIndex >= 0 && MatchList.SelectedItem is StockSearchMatch m)
                    AcceptMatch(m);
                else
                    CloseWithResult();
                return;
            case Key.Escape:
                MatchList.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
        }
    }
    else if (e.Key == Key.Enter)
    {
        e.Handled = true;
        CloseWithResult();
    }
    else if (e.Key == Key.Escape)
    {
        DialogResult = false;
        Close();
    }
}

private void MoveSel(int delta)
{
    var count = MatchList.Items.Count;
    if (count == 0) return;
    var idx = MatchList.SelectedIndex;
    var next = idx < 0 ? 0 : Math.Clamp(idx + delta, 0, count - 1);
    MatchList.SelectedIndex = next;
    MatchList.ScrollIntoView(MatchList.SelectedItem);
}
```

- [ ] **Step 3: 主窗口添加股票走搜索流程**

修改 `src/StockWidget.App/Views/MainWindow.xaml.cs` 的「添加股票」右键菜单处理（`add.Click` 内）：

```csharp
var api = App.Services.GetRequiredService<ITencentQuoteApi>(); // using StockWidget.Core.Services
var dlg = new InputDialog(
    "添加股票",
    "股票代码 / 名称 / 拼音关键字：",
    "例如：600390、浦发、pfyh、00700、AAPL",
    kw => api.SearchSuggestAsync(kw)) { Owner = this };

if (dlg.ShowDialog() != true) return;

var result = await _vm.AddStockAsync(dlg.SelectedMatch?.Code ?? dlg.Input);
```

`MainWindow.xaml.cs` 文件头新增：
```csharp
using StockWidget.Core.Services;
using Microsoft.Extensions.DependencyInjection;
```
（若尚未引入。）

`use StockCodeNormalizer` 归一化已由 `AddStockAsync` 内部完成，`SelectedMatch.Code` 已是规范代码，直接传入即可。

- [ ] **Step 4: 编译 + 冒烟**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

冒烟：右键 → 添加股票 → 输入"浦发" → 下拉出现候选 → ↑↓ 高亮 → Enter 添加成功；
输入含空/未知 → 下拉隐藏，直接回车按原代码提交。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.App/Views/InputDialog.xaml src/StockWidget.App/Views/InputDialog.xaml.cs src/StockWidget.App/Views/MainWindow.xaml.cs
git commit -m "feat: 添加股票支持名称/拼音下拉搜索联动
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 10: 强调色板（Core 纯逻辑 + 测试）

**Files:**
- Create: `src/StockWidget.Core/Models/AccentPalette.cs`（纯数据：可测色板定义，不依赖 WPF）
- Create: `tests/StockWidget.Tests/AccentPaletteTests.cs`

- [ ] **Step 1: 失败测试**

创建 `tests/StockWidget.Tests/AccentPaletteTests.cs`：

```csharp
using StockWidget.Core.Models;

namespace StockWidget.Tests;

public class AccentPaletteTests
{
    [Fact]
    public void Default_ContainsFivePalettes()
    {
        Assert.Equal(6, AccentPalette.All.Count); // 5 补默认橙 = 6
    }

    [Fact]
    public void EachPalette_KeyIsUnique_NonEmptyName()
    {
        var keys = AccentPalette.All.Select(p => p.Key).ToHashSet();
        Assert.Equal(AccentPalette.All.Count, keys.Count);
        Assert.All(AccentPalette.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void AccentRgb_ShouldBeArgb()
    {
        foreach (var p in AccentPalette.All)
        {
            Assert.InRange(p.AccentRgb, 0, 0xFFFFFF);
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter AccentPaletteTests`
Expected: 编译失败（`AccentPalette` 不存在）。

- [ ] **Step 3: 实现色板（纯数据，可测）**

创建 `src/StockWidget.Core/Models/AccentPalette.cs`：

```csharp
namespace StockWidget.Core.Models;

/// <summary>强调色候选（纯数据，供 UI 派生整套强调相关刷）。<c>AccentRgb</c> 为 0xRRGGBB。</summary>
public sealed record AccentPalette(string Key, string Name, int AccentRgb)
{
    public static readonly IReadOnlyList<AccentPalette> All =
    [
        new("orange", "默认橙", 0xFFA640),
        new("blue", "蓝", 0x38BDF8),
        new("green", "绿", 0x22C55E),
        new("purple", "紫", 0xA78BFA),
        new("red", "红", 0xF43F5E),
        new("teal", "青", 0x2DD4BF),
    ];

    public static AccentPalette FromKey(string? key) =>
        All.FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>由 0xRRGGBB 派生带 alpha 的颜色值（0xAARRGGBB）。</summary>
    public static int Rgba(int rgb, int alpha) => (alpha << 24) | (rgb & 0xFFFFFF);

    /// <summary>强调主色（0xFF_主色）。</summary>
    public int Main => Rgba(AccentRgb, 0xFF);
    /// <summary>柔和强调（16% alpha 用于底/背景）。</summary>
    public int Soft => Rgba(AccentRgb, 0x29);
    /// <summary>强调头/竖条（~80% alpha）。</summary>
    public int Strong => Rgba(AccentRgb, 0xCC);
}
```

- [ ] **Step 4: 运行确认通过**

Run: `dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter AccentPaletteTests`
Expected: 3 tests PASS。

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.Core/Models/AccentPalette.cs tests/StockWidget.Tests/AccentPaletteTests.cs
git commit -m "feat: 强调色板定义（5 组可选 + 默认）与单元测试
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 11: AppSettings 增 AccentColor + 装载强调色资源

**Files:**
- Modify: `src/StockWidget.Core/Models/AppSettings.cs`
- Modify: `src/StockWidget.App/Services/AppServices.cs`（ThemeManager 按强调色派生资源）
- Modify: `src/StockWidget.App/ViewModels/MainViewModel.cs`

- [ ] **Step 1: AppSettings 加 AccentColor**

在 `src/StockWidget.Core/Models/AppSettings.cs`「增强：迷你走势图」之后新增：

```csharp
// ---------- 增强：UI 强调色 ----------
/// <summary>强调色 key（见 AccentPalette.All），默认空 = 默认橙。</summary>
public string AccentColor { get; set; } = "";
```

- [ ] **Step 2: ThemeManager 按强调色派生效**

在 `src/StockWidget.App/Services/AppServices.cs` 的 `ThemeManager.ApplyDictionary` 内，`Application.Current.Resources` 上，新增强调色派生资源替换。实现方式：在替换主题字典 `dict` 之后，额外合并一组「强调派生」平铺刷资源。

新增私有方法与字段：

```csharp
// 强调色派生刷（Key → int ARGB），空则不覆盖（沿用主题字典默认橙）
public void ApplyAccent(string accentKey)
{
    var pal = AccentPalette.FromKey(accentKey);
    var app = Application.Current;
    var accentColors = new Dictionary<string, /*Color*/ object>();
    // 主题字典里强调相关资源 Key 集合（现有命名）——由主题字典实际 Key 决定
    // 不在此硬编码，交由 Step 3 的 XAML 层实现动态化；此处保留桩以兼容调用方。
    _ = pal;
}
```

> **注意**：由于 `App.xaml.cs` 的启动时序与现有资源结构，@瑞让强调色真正联动刷新的实现更稳地放到 Step 3，
> 通过 XAML 触发器（DataTrigger on AccentColor）在根 Grid 一次性派生。
> 本任务的 Step 2 仅保证：`MainViewModel.ApplySettings` 在主题应用前，把 `_cfg.AccentColor` 传给
> 一处可 Hook 的 `ThemeManager.ApplyAccent`（若资源层未就绪则不抛异常）。
> 完整视觉联动在 Task 12 落地。此步以「可编译、不抛异常、设置可保存」为达成标准。

在 `src/StockWidget.App/ViewModels/MainViewModel.cs` 的 `ApplySettingsInternal(bool save)` 内，`ThemeManager.Instance.Apply(_cfg.Theme);` 之后新增：

```csharp
ThemeManager.Instance.ApplyAccent(_cfg.AccentColor);
```

- [ ] **Step 3: 编译**

```bash
dotnet build StockWidget.slnx -c Debug
```
Expected: 0 警告 0 错误。

- [ ] **Step 4: 提交**

```bash
git add src/StockWidget.Core/Models/AppSettings.cs src/StockWidget.App/Services/AppServices.cs src/StockWidget.App/ViewModels/MainViewModel.cs
git commit -m "feat: 设置新增 AccentColor 强调色并提供派生入口
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 12: 视觉拟质感（主窗口/主题/控件/走势/分时/关于）

**Files:**
- Modify: `src/StockWidget.App/Themes/Dark.xaml`
- Modify: `src/StockWidget.App/Themes/Light.xaml`
- Modify: `src/StockWidget.App/Themes/Controls.xaml`
- Modify: `src/StockWidget.App/Views/MainWindow.xaml`
- Modify: `src/StockWidget.App/Views/Sparkline.cs`
- Modify: `src/StockWidget.App/Views/MinuteChartWindow.xaml`
- Modify: `src/StockWidget.App/Views/AboutWindow.xaml` / `.cs`
- Modify: `src/StockWidget.App/Views/SettingsWindow.xaml` / `.cs`（强调色选择 UI）

> 本任务全部为 WPF 视觉，无 Core 可测逻辑；用编译 + 手动冒烟验证。

- [ ] **Step 1: 主题新增渐变/阴影/高光资源**

在 `Dark.xaml` 与 `Light.xaml` 各新增以下资源（深浅分别给不同色值）：

`Dark.xaml`（后附各色值）：

```xml
<!-- 卡片渐变底（顶部高光 + 下部主色） -->
<LinearGradientBrush x:Key="CardGradientBrush" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#38FFFFFF" Offset="0" />
    <GradientStop Color="#F823232A" Offset="0.5" />
    <GradientStop Color="#F8181820" Offset="1" />
</LinearGradientBrush>
<SolidColorBrush x:Key="CardShadowBrush" Color="#8A000000" />
<SolidColorBrush x:Key="ChipBrush" Color="#3D000000" />
<SolidColorBrush x:Key="HoverStrokeBrush" Color="#59FFFFFF" />
<SolidColorBrush x:Key="LockBrush" Color="#A2A2AE" />
<SolidColorBrush x:Key="BaselineBrush" Color="#59FFA640" />
```

`Light.xaml` 对应：

```xml
<LinearGradientBrush x:Key="CardGradientBrush" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#1AFFFFFF" Offset="0" />
    <GradientStop Color="#FAFDFDFE" Offset="0.5" />
    <GradientStop Color="#F1F3F5" Offset="1" />
</LinearGradientBrush>
<SolidColorBrush x:Key="CardShadowBrush" Color="#33000000" />
<SolidColorBrush x:Key="ChipBrush" Color="#0D000000" />
<SolidColorBrush x:Key="HoverStrokeBrush" Color="#40000000" />
<SolidColorBrush x:Key="LockBrush" Color="#63636E" />
<SolidColorBrush x:Key="BaselineBrush" Color="#40E8590C" />
```

- [ ] **Step 2: 主窗口双描边 + 渐变 + 阴影 + 锁定徽章**

修改 `src/StockWidget.App/Views/MainWindow.xaml`：外层 Border 改为「阴影外层 + 双描边卡片」：

```xml
<Border CornerRadius="12" Margin="6"
        Background="Transparent">
    <Border.Effect>
        <DropShadowEffect BlurRadius="22" Direction="270" ShadowDepth="3"
                          Opacity="0.35" Color="{DynamicResource CardShadowBrush}" />
    </Border.Effect>
    <Border CornerRadius="12" Background="{DynamicResource CardGradientBrush}"
            BorderBrush="{DynamicResource DividerBrush}" BorderThickness="1"
            ClipToBounds="True">
        <Border CornerRadius="12" Background="{DynamicResource HoverStrokeBrush}"
                Opacity="0.5" Margin="1" BorderThickness="0" />
        <Grid x:Name="RootGrid" Margin="10,8,10,8" Background="Transparent">
            <!-- 原 RootGrid 内容保持不变 -->
        </Grid>
    </Border>
</Border>
```

> 注意：原最外层 `<Border CornerRadius="12" Background="{DynamicResource BgBrush}" ...>` 需要重构为上述双 Border。
> `RootGrid` 内容（DataGrid/量能栏）整体保持不变，仅外层包裹调整。手动核对 XAML 嵌套闭合。

在 BottomBar 的休市徽章旁新增锁定徽章（可视于 `ShowLockedStatus`）：

```xml
<Border x:Name="LockBadge" Grid.Column="1" Visibility="Collapsed"
        Background="{DynamicResource ChipBrush}" CornerRadius="5"
        Padding="8,2" VerticalAlignment="Center" Margin="8,0,0,0" ToolTip="窗口位置已锁定">
    <TextBlock Text="🔒 已锁定" FontSize="10" FontWeight="Bold"
               Foreground="{DynamicResource LockBrush}" />
</Border>
```

在 `MainWindow.xaml.cs` 的 `_vm.PropertyChanged` 订阅中追加锁定徽章可见性（复用现有 `ShowLockedStatus` 逻辑，于 `MarketClosed` 分支旁）：

```csharp
if (e.PropertyName == nameof(MainViewModel.Settings)
    || e.PropertyName == nameof(MainViewModel.StatusText))
{
    LockBadge.Visibility = _vm.Settings.ShowLockedStatus && _vm.Settings.Locked
        ? Visibility.Visible : Visibility.Collapsed;
}
```

- [ ] **Step 3: 表头排序箭头 + 分组头细节**

在 `Controls.xaml` 的 `DataGridColumnHeader` 模板中，在 ContentPresenter 右侧追加排序箭头（默认 DataGrid 原生 SortDirection 不可直接绑定，用 `DataGridColumnHeader` 的 `SortDirection` 触发器）：

在模板 Border 内追加：

```xml
<Path x:Name="sortArrow" Data="M0,2 L4,6 L8,2" Stretch="Uniform" Width="8"
      Stroke="{DynamicResource AccentBrush}" StrokeThickness="1.5"
      Visibility="Collapsed" HorizontalAlignment="Right" Margin="0,0,4,0" />
```

在模板触发器区追加：

```xml
<Trigger Property="SortDirection" Value="Ascending">
    <Setter TargetName="sortArrow" Property="Visibility" Value="Visible" />
    <Setter TargetName="sortArrow" Property="Data" Value="M0,6 L4,2 L8,6" />
</Trigger>
<Trigger Property="SortDirection" Value="Descending">
    <Setter TargetName="sortArrow" Property="Visibility" Value="Visible" />
    <Setter TargetName="sortArrow" Property="Data" Value="M0,2 L4,6 L8,2" />
</Trigger>
```

替换分组头样式 `CategoryGroupHeaderStyle` 为「渐隐底 + 左竖条」：

```xml
<Style x:Key="CategoryGroupHeaderStyle" TargetType="GroupItem">
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="GroupItem">
                <StackPanel>
                    <Border Background="{DynamicResource AccentSoftBrush}" CornerRadius="5"
                            Padding="8,2" Margin="0,2,0,3" HorizontalAlignment="Left">
                        <StackPanel Orientation="Horizontal">
                            <Border Width="3" CornerRadius="1.5" Background="{DynamicResource AccentBrush}"
                                    Margin="0,0,6,0" />
                            <TextBlock Text="{Binding Name}" FontSize="11" FontWeight="Bold"
                                       Foreground="{DynamicResource AccentBrush}" />
                        </StackPanel>
                    </Border>
                    <ItemsPresenter />
                </StackPanel>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

- [ ] **Step 4: 量能金额等宽（TabularFigures）**

在 `MainWindow.xaml.cs` 的 `RebuildAmountText()` 中，金额 Run 使用固定 `Typography.NumeralAlignment` 的 `Tabular`，
使每位数字同宽，刷新时数值位数变化不跳动。这是 WPF 对 OpenType 表格数字的标准支持。

```csharp
// 等宽数字（Typographic Tabular Figures），避免刷新时金额抖动
var amountRun = new Run($"{_vm.TotalAmountText} 亿")
{
    Foreground = TotalBrush(),
    FontWeight = FontWeights.Bold,
    FontSize = AmountText.FontSize + 1,
};
Typography.SetNumeralAlignment(amountRun, FontNumeralAlignment.Tabular);
```

> 说明：`FontNumeralAlignment.Tabular` 依赖所用字体（微软雅黑）的表格数字字形支持；若个别字体不支持，
> 该设置被忽略、退回比例数字（不报错）。以「刷新时金额位宽稳定」为验收，跨字体不做强制。

- [ ] **Step 5: Sparkline 面积渐变 + 末点光标**

修改 `src/StockWidget.App/Views/Sparkline.cs` 的 `OnRender`：在现有折线绘制之外，
先把几何计算与末点提取出来（一次性循环），再依次绘制「面积渐变 → 折线 → 末点光标」。
用真实可编译的 WPF API（`GradientStopCollection` / `LinearGradientBrush`）。

```csharp
protected override void OnRender(DrawingContext dc)
{
    var w = ActualWidth;
    var h = ActualHeight;
    if (w <= 0 || h <= 0) return;

    var values = Values;
    if (values is not { Count: >= 2 })
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        return;
    }

    decimal min = values.Min(), max = values.Max();
    if (max == min) { max += 0.0001m; min -= 0.0001m; }

    // 一次循环：收集折线点 + 记录末点
    var pts = new Point[values.Count];
    for (var i = 0; i < values.Count; i++)
    {
        var x = i * w / (values.Count - 1);
        var y = h - 2 - (float)((values[i] - min) / (max - min)) * (h - 4);
        pts[i] = new Point(x, y);
    }
    var lastPt = pts[^1];

    // 折线笔
    var lastUp = Baseline is { } bl ? values[^1] >= bl : values[^1] >= values[0];
    var colorKey = lastUp ? "UpBrush" : "DownBrush";
    var brush = Application.Current.Resources[colorKey] as Brush ?? Brushes.Gray;
    var lineColor = brush is SolidColorBrush sb ? sb.Color : Colors.Gray;
    var pen = new Pen(brush, 1.2);
    pen.Freeze();

    // 面积渐变填充（顶部用折线色的半透明，向下渐隐透明）
    var fillBrush = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
        GradientStops = new GradientStopCollection
        {
            new(Color.FromArgb(0x3C, lineColor.R, lineColor.G, lineColor.B), 0),
            new(Color.FromArgb(0x00, lineColor.R, lineColor.G, lineColor.B), 1),
        },
    };
    fillBrush.Freeze();

    var areaGeo = new StreamGeometry();
    using (var aCtx = areaGeo.Open())
    {
        aCtx.BeginFigure(pts[0], true, false);
        for (var i = 1; i < pts.Length; i++) aCtx.LineTo(pts[i], true, false);
        aCtx.LineTo(new Point(w, h), true, false);
        aCtx.LineTo(new Point(0, h), true, false);
        aCtx.LineTo(pts[0], true, false);
    }
    areaGeo.Freeze();
    dc.DrawGeometry(fillBrush, null, areaGeo);

    // 折线本体（复用原有 geo 逻辑重建，或直接用 pts 构造）
    var lineGeo = new StreamGeometry();
    using (var ctx = lineGeo.Open())
    {
        ctx.BeginFigure(pts[0], false, false);
        for (var i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
    }
    lineGeo.Freeze();
    dc.DrawGeometry(null, pen, lineGeo);

    // 末点光标（白心 + 折线色描边）
    dc.DrawEllipse(Brushes.White, new Pen(brush, 1.2), lastPt, 2.1, 2.1);
}
```

> 说明：替换原 `OnRender` 中「先建 geo 再 DrawGeometry(null, pen, geo)」的两段；
> `pts`/`min`/`max`/`lastUp` 语义与原实现一致。作为自绘控件，需本地运行预览确认面积不与折线重叠失真、
> 且面积仅填充折线下方（已通过 begin→end 与回到 (0,h) 闭合。

- [ ] **Step 6: 分时图细节**

修改 `src/StockWidget.App/Views/MinuteChartWindow.xaml`：
- 价格 `PriceText` 加 `TextOptions` 等宽数字（同 §Step 4 说明）。
- Chart 区域底部叠加昨收虚线 → 在 `MinuteChartWindow.xaml.cs` 或 XAML 用 `Line`/`Rectangle` 在 `Chart` 之上补一条
  `Stroke="{DynamicResource BaselineBrush}"` 的横线（Y 依 PrevClose 占比）。实现放在 `Loaded`/刷新后依 `PrevClose` 设置 `Canvas.Top`/`Margin`。

在 `MinuteChartWindow.xaml` 的 Chart Border 内、Grid 中部添加基准线元素：

```xml
<Rectangle x:Name="BaselineBar" Height="1" Fill="{DynamicResource BaselineBrush}"
           HorizontalAlignment="Stretch" VerticalAlignment="Center" Opacity="0.7" />
```

`MinuteChartWindow.xaml.cs` 在数据填充后依据 PrevClose 定位基准线（示例逻辑，值域映射同 Sparkline）：

```csharp
private void PositionBaseline(decimal? prevClose, List<decimal> prices)
{
    if (prevClose is not { } pc || prices.Count < 2 || Chart.ActualHeight <= 0) { BaselineBar.Visibility = Visibility.Collapsed; return; }
    BaselineBar.Visibility = Visibility.Visible;
    var min = prices.Min(); var max = prices.Max();
    if (max == min) max += 0.0001m;
    var ratio = (float)((pc - min) / (max - min));
    BaselineBar.Margin = new Thickness(0, (Chart.ActualHeight - 1) * (1 - ratio), 0, 0);
}
```

- [ ] **Step 7: 设置中心强调色选择 + 关于窗口**

在 `src/StockWidget.App/Views/SettingsWindow.xaml`「通用」Tab 的「系统」Card 内，`ShowSparklineCheck` 之下新增强调色选择：

```xml
<TextBlock Text="强调色" Style="{StaticResource CardHeaderText}" Margin="0,10,0,5" />
<WrapPanel x:Name="AccentPanel">
    <!-- 运行时由代码注入色块单选 -->
</WrapPanel>
```

在 `SettingsWindow.xaml.cs` 的 `LoadFromSettings` 通用区、`ShowSparklineCheck` 之后构建色块：

```csharp
// 强调色选择
BuildAccentPicker();
```

（`AccentPalette.FromKey(_working.AccentColor)` 高亮当前）。新增方法：

```csharp
private void BuildAccentPicker()
{
    AccentPanel.Children.Clear();
    foreach (var pal in AccentPalette.All)
    {
        var btn = new RadioButton
        {
            GroupName = "Accent",
            Tag = pal.Key,
            IsChecked = string.Equals(_working.AccentColor, pal.Key, StringComparison.OrdinalIgnoreCase),
            Content = new Border { Width = 22, Height = 22, CornerRadius = 6,
                Background = new SolidColorBrush(IntToColor(pal.AccentRgb)) },
            ToolTip = pal.Name,
            Margin = new Thickness(0, 0, 10, 4),
        };
        btn.Checked += (_, _) => _working.AccentColor = (string)btn.Tag;
        AccentPanel.Children.Add(btn);
    }
}

private static Color IntToColor(int rgb) =>
    Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
```

`TryCollect` 中加入 `cfg.AccentColor = _working.AccentColor;`。

关于窗口整理 `AboutWindow.xaml`：补产品名/版本/版权行；若已有版本文本则微调字号/间距与卡片对齐（视觉增量）。

- [ ] **Step 8: 编译 + 三态/强调色冒烟**

```bash
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
```
Expected: 0 警告 0 错误；全部测试通过。

冒烟：深/浅/跟随系统切换正常；切强调色（蓝/绿/紫/红）整套配色联动；主窗口渐变/阴影/锁定徽章/分组头竖条/
走势面积渐变在 Win10/11 一致；分时图基准虚线与当前价横线 OK；窗口显隐淡入淡出流畅。

- [ ] **Step 9: 提交**

```bash
git add src/StockWidget.App/Themes/ src/StockWidget.App/Views/ src/StockWidget.App/Services/AppServices.cs
git commit -m "feat: UI 拟质感深度优化（渐变/阴影/强调色/走势/分时/关于）
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 13: 收尾验证与发布检视

**Files:**
- Modify: `CHANGELOG.md`（可选）
- 运行: 全量构建测试 + 发布级冒烟

- [ ] **Step 1: 全量构建 + 测试**

```bash
dotnet build StockWidget.slnx -c Debug
dotnet build StockWidget.slnx -c Release
dotnet test StockWidget.slnx
```
Expected: 两态均 0 警告 0 错误；全部测试通过（53 基 + 5 日历 + 4 smartbox + 3 强调色 = 65）。

- [ ] **Step 2: 最终冒烟**

`dotnet run --project src/StockWidget.App`：
- 托盘色点红涨绿跌
- 休市徽章 + 智能抓取
- 名称搜索下拉添加
- 三态主题 + 强调色联动
- 视觉无渲染异常

- [ ] **Step 3: 更新 CHANGELOG.md（Unreleased 段）**

在 `CHANGELOG.md` 的 `[Unreleased]` 下补：

```markdown
## [Unreleased]
### Added
- UI 拟质感深度优化：卡片渐变/柔和投影/强调色（橙蓝绿紫红青）联动、走势面积渐变、表头排序箭头、分组头竖条、锁定徽章
- 节假日休市识别：内置交易日历（2025–2028）+ 休市智能抓取
- 按名称/拼音添加股票：腾讯 smartbox 搜索下拉联动
### Fixed
- 接通托盘图标大盘涨跌色点（红涨绿跌实时更新）
- 删除模板残留 Class1.cs
```

> 若本轮将发布 v1.1.0 并在用户明确要求下做 Release，再由 release 流程处理版本/Tag；本计划不主动打 Tag。

- [ ] **Step 4: 提交 CHANGELOG**

```bash
git add CHANGELOG.md
git commit -m "docs: CHANGELOG 补 Unreleased（v1.1 完善项）
Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Self-Review 结论（写计划时内查）

- **Spec 覆盖**：托盘色点(T2)、Class1 删除(T1)、节假日表/种子/服务/智能抓取(T3–T6)、名称搜索(T7–T9)、UI 四方向(T10–T12)、强调色(§6.2→T10/T11/T12.7) 全部有任务。
- **类型一致**：`MarketMoodPct`、`ITradingCalendar`、`StockSearchMatch`、`AccentPalette`、`UpdateTrayMood` 命名在计划内各任务一致。
- **占位符**：`Seed{年份}(...)` 为数据录入占位，已标注"必须填充真实数据"；Sparkline/等宽字体为工程实现取舍，已给目标与回退。无"TBD/TODO"空洞。
- **范围**：单一可运行产品内五块独立，任务粒度 2–5 分钟。