# 日K归档 + K线图 + 成交额趋势 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交易日收盘后自动归档全自选 OHLC 到日K表；分时窗口切换查看 K 线（蜡烛+量副图+均线+区间涨跌）；新增沪深成交额 60 日趋势窗口。

**Architecture:** Core 层新增 `daily_kline` 表（联合主键 Code+Date）+ `IDailyKlineRepository` + `KlineArchiver`（归档条件判定服务，时间作为参数传入以便测试）；归档挂接在 `MainViewModel.RefreshAsync` 成功块尾部、`Task.Run` 后台执行（吸取 LoadSparklines UI 阻塞教训）。App 层两个自绘控件（`KLineChart`/`AmountTrendChart`，DrawingContext 风格同 Sparkline，未冻结画刷读主题资源），分时窗口加页签，成交额趋势窗口单实例复用（同分时窗口模式）。

**Tech Stack:** C# / WPF (.NET 10)、EF Core 10 SQLite 迁移、xUnit、CommunityToolkit.Mvvm。

**基线：** v0.1.2（HEAD），69 测试。**关键教训**：UI 线程禁止同步 DB 查询；Brush 读取主题资源后 Freeze。

---

## 前置准备

- [ ] **Step 0: 基线验证**

```bash
taskkill //IM StockWidget.App.exe //F 2>/dev/null; dotnet build StockWidget.slnx -c Debug 2>&1 | tail -3 && dotnet test StockWidget.slnx 2>&1 | tail -2
```
Expected: 0 警告 0 错误；69 通过。git status 干净。

---

## Task 1: daily_kline 表（实体 + DbContext + EF 迁移）

**Files:** Modify `src/StockWidget.Core/Data/Entities/Entities.cs`、`src/StockWidget.Core/Data/StockWidgetDbContext.cs`；Create 迁移（dotnet-ef 生成）

- [ ] **Step 1: 实体**（Entities.cs 末尾追加）

```csharp
/// <summary>daily_kline 表：日K线（每股每日一根，收盘归档）。</summary>
public sealed class DailyKlineEntity
{
    /// <summary>规范化代码（联合主键）。</summary>
    public string Code { get; set; } = "";

    /// <summary>交易日期 yyyy-MM-dd（联合主键）。</summary>
    public string Date { get; set; } = "";

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }

    /// <summary>成交量（手，腾讯口径）。</summary>
    public decimal Volume { get; set; }

    /// <summary>成交额（万元，腾讯口径）。</summary>
    public decimal Amount { get; set; }
}
```

- [ ] **Step 2: DbContext**（DbSet 区 + OnModelCreating 尾部）

```csharp
public DbSet<DailyKlineEntity> DailyKlines => Set<DailyKlineEntity>();
```
```csharp
mb.Entity<DailyKlineEntity>(e =>
{
    e.ToTable("daily_kline");
    e.HasKey(x => new { x.Code, x.Date });
});
```

- [ ] **Step 3: 迁移**（从仓库根，注意 Core 非启动项目需指定 startup）

```bash
dotnet tool restore
dotnet ef migrations add AddDailyKline --project src/StockWidget.Core --startup-project src/StockWidget.App
```
Expected: 生成 `<ts>_AddDailyKline.cs`（CreateTable，复合主键 Code+Date）+ 快照更新。

- [ ] **Step 4: 构建** `dotnet build StockWidget.slnx -c Debug` → 0 警告 0 错误

- [ ] **Step 5: 提交**

```bash
git add src/StockWidget.Core/Data/ src/StockWidget.Core/Migrations/
git commit -m "feat: 新增 daily_kline 日K表与 EF 迁移" -m "Co-Authored-By: Claude Code <noreply@anthropic.com>"
```

---

## Task 2: IDailyKlineRepository（TDD）

**Files:** Modify `src/StockWidget.Core/Services/Repositories.cs`（追加接口+实现，与现有仓储同文件同风格）；Create `tests/StockWidget.Tests/DailyKlineRepositoryTests.cs`

- [ ] **Step 1: 失败测试**

```csharp
using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class DailyKlineRepositoryTests : DatabaseTestBase
{
    private static DailyKlineEntity K(string code, string date, decimal close) =>
        new() { Code = code, Date = date, Open = close, High = close, Low = close, Close = close, Volume = 100, Amount = 50 };

    [Fact]
    public void UpsertRange_IsIdempotent()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("sh600390", "2026-09-16", 10m)]);
        repo.UpsertRange([K("sh600390", "2026-09-16", 11m)]); // 同键覆盖
        var all = repo.GetByCode("sh600390", 10);
        Assert.Single(all);
        Assert.Equal(11m, all[0].Close);
    }

    [Fact]
    public void GetByCode_ReturnsAscendingLatestN()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        repo.UpsertRange([K("sh600390", "2026-09-14", 10m), K("sh600390", "2026-09-15", 11m), K("sh600390", "2026-09-16", 12m)]);
        var rows = repo.GetByCode("sh600390", 2);
        Assert.Equal(2, rows.Count);              // 最近 2 根
        Assert.Equal("2026-09-15", rows[0].Date); // 升序返回
        Assert.Equal("2026-09-16", rows[1].Date);
        Assert.Empty(repo.GetByCode("sz000001", 10)); // 无数据返回空
    }

    [Fact]
    public void GetArchivedCodesToday_ReturnsTodayOnly()
    {
        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        repo.UpsertRange([K("sh600390", today, 10m), K("sh600390", "2026-01-01", 10m)]);
        var codes = repo.GetArchivedCodesToday();
        Assert.Contains("sh600390", codes);
    }
}
```

- [ ] **Step 2: 确认编译失败**（`dotnet test tests/StockWidget.Tests/StockWidget.Tests.csproj --filter DailyKlineRepositoryTests` → CS0246）

- [ ] **Step 3: 实现**（Repositories.cs 末尾追加）

```csharp
public interface IDailyKlineRepository
{
    /// <summary>批量 upsert（按 Code+Date 存在则覆盖）。</summary>
    void UpsertRange(IEnumerable<DailyKlineEntity> klines);

    /// <summary>今日（DateTime.Today）已归档的代码集合。</summary>
    HashSet<string> GetArchivedCodesToday();

    /// <summary>某代码最近 N 根日K，按日期升序返回。</summary>
    List<DailyKlineEntity> GetByCode(string code, int days);
}

public sealed class DailyKlineRepository(IDbContextFactory<StockWidgetDbContext> dbFactory) : IDailyKlineRepository
{
    public void UpsertRange(IEnumerable<DailyKlineEntity> klines)
    {
        var list = klines.ToList();
        if (list.Count == 0) return;
        using var db = dbFactory.CreateDbContext();
        var keys = list.Select(k => (k.Code, k.Date)).ToHashSet();
        var existing = db.DailyKlines
            .Where(k => keys.Contains(new { k.Code, k.Date }))
            .ToDictionary(k => (k.Code, k.Date));
        foreach (var k in list)
        {
            if (existing.TryGetValue((k.Code, k.Date), out var row))
            {
                row.Open = k.Open; row.High = k.High; row.Low = k.Low; row.Close = k.Close;
                row.Volume = k.Volume; row.Amount = k.Amount;
            }
            else
            {
                db.DailyKlines.Add(k);
            }
        }
        db.SaveChanges();
    }

    public HashSet<string> GetArchivedCodesToday()
    {
        using var db = dbFactory.CreateDbContext();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return db.DailyKlines.Where(k => k.Date == today)
            .Select(k => k.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public List<DailyKlineEntity> GetByCode(string code, int days)
    {
        using var db = dbFactory.CreateDbContext();
        return db.DailyKlines.AsNoTracking()
            .Where(k => k.Code == code)
            .OrderByDescending(k => k.Date)
            .Take(days)
            .OrderBy(k => k.Date)
            .ToList();
    }
}
```
（文件需有 `using Microsoft.EntityFrameworkCore;` 与 `using StockWidget.Core.Data.Entities;`——Repositories.cs 头部已具备。）

- [ ] **Step 4: 测试通过**（3 PASS）+ 全量构建 0 警告 0 错误

- [ ] **Step 5: 提交** `feat: 日K仓储 UpsertRange/GetByCode/GetArchivedCodesToday`

---

## Task 3: KlineArchiver 归档服务（TDD）

**Files:** Create `src/StockWidget.Core/Services/KlineArchiver.cs`；Create `tests/StockWidget.Tests/KlineArchiverTests.cs`；Modify `ServiceCollectionExtensions.cs`

- [ ] **Step 1: 失败测试**

```csharp
using Microsoft.Extensions.DependencyInjection;
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;
using StockWidget.Core.Services;

namespace StockWidget.Tests;

public class KlineArchiverTests : DatabaseTestBase
{
    private static QuoteData Q(string code, decimal close) => new()
    {
        Code = code, Name = "测试", Price = close, Open = close, High = close, Low = close,
        Close = close, Volume = 1000, Amount = 500, Success = true,
    };

    [Fact]
    public void TradingDay_After1505_ArchivesAllSuccessQuotes()
    {
        var now = new DateTime(2026, 9, 16, 15, 6, 0); // 周三
        var archiver = MakeArchiver(now);
        archiver.TryArchive([Q("sh600390", 10m), QuoteData.Failed("sz000001")], now);

        var repo = Provider.GetRequiredService<IDailyKlineRepository>();
        var rows = repo.GetByCode("sh600390", 5);
        Assert.Single(rows);            // 失败占位不归档
        Assert.Equal(10m, rows[0].Close);
        Assert.Empty(repo.GetByCode("sz000001", 5));
    }

    [Fact]
    public void Before1505_Skips()
    {
        var now = new DateTime(2026, 9, 16, 14, 0, 0);
        MakeArchiver(now).TryArchive([Q("sh600390", 10m)], now);
        Assert.Empty(Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5));
    }

    [Fact]
    public void NonTradingDay_Skips()
    {
        var now = new DateTime(2026, 9, 19, 15, 6, 0); // 周六（空日历兜底为非交易日）
        MakeArchiver(now).TryArchive([Q("sh600390", 10m)], now);
        Assert.Empty(Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5));
    }

    [Fact]
    public void ReArchive_SameDay_OverwritesNotDuplicates()
    {
        var now = new DateTime(2026, 9, 16, 15, 6, 0);
        var archiver = MakeArchiver(now);
        archiver.TryArchive([Q("sh600390", 10m)], now);
        archiver.TryArchive([Q("sh600390", 11m)], now); // 二次归档覆盖
        var rows = Provider.GetRequiredService<IDailyKlineRepository>().GetByCode("sh600390", 5);
        Assert.Single(rows);
        Assert.Equal(11m, rows[0].Close);
    }

    private KlineArchiver MakeArchiver(DateTime now)
    {
        var calendar = new TradingCalendarService(TradingCalendarSeeder.GetBuiltIn());
        return new KlineArchiver(Provider.GetRequiredService<IDailyKlineRepository>(), calendar);
    }
}
```
（注意：2026-09-16 为周三、09-19 为周六；db 里只有 2026-01-01/2025 系列种子，测试日期由兜底规则判定。）

- [ ] **Step 2: 确认失败**（KlineArchiver 不存在）

- [ ] **Step 3: 实现**（KlineArchiver.cs）

```csharp
using StockWidget.Core.Data.Entities;
using StockWidget.Core.Models;

namespace StockWidget.Core.Services;

public interface IKlineArchiver
{
    /// <summary>满足条件（交易日、15:05 后、当日未归档）时将行情 upsert 进日K表；否则静默跳过。</summary>
    void TryArchive(IReadOnlyList<QuoteData> quotes, DateTime now);
}

/// <summary>收盘归档：刷新链路已在 15:05 后抓到全自选 OHLC，直接落库，零额外请求。</summary>
public sealed class KlineArchiver(
    IDailyKlineRepository klines,
    ITradingCalendar calendar) : IKlineArchiver
{
    private static readonly TimeSpan ArchiveAfter = new(15, 5, 0);

    public void TryArchive(IReadOnlyList<QuoteData> quotes, DateTime now)
    {
        try
        {
            if (!calendar.IsTodayTrading()) return;
            if (now.TimeOfDay < ArchiveAfter) return;

            var today = now.ToString("yyyy-MM-dd");
            var archived = klines.GetArchivedCodesToday();
            var missing = quotes
                .Where(q => q.Success && q.Close is not null && !archived.Contains(q.Code))
                .Select(q => new DailyKlineEntity
                {
                    Code = q.Code,
                    Date = today,
                    Open = q.Open ?? q.Price ?? 0m,
                    High = q.High ?? q.Price ?? 0m,
                    Low = q.Low ?? q.Price ?? 0m,
                    Close = q.Price ?? 0m,
                    Volume = q.Volume ?? 0m,
                    Amount = q.Amount ?? 0m,
                })
                .ToList();
            if (missing.Count == 0) return;
            klines.UpsertRange(missing);
        }
        catch
        {
            // 归档失败静默，下个刷新 tick 重试
        }
    }
}
```

- [ ] **Step 4: DI 注册**（ServiceCollectionExtensions，`services.AddSingleton<IKlineArchiver, KlineArchiver>();` 放 `ITradingCalendar` 注册之后）

- [ ] **Step 5: 测试 4 PASS + 全量构建；提交** `feat: 收盘归档服务 KlineArchiver（15:05 后零额外请求落库）`

---

## Task 4: 挂接归档 + GetRecent（TDD）

**Files:** Modify `Repositories.cs`（IAmountHistoryRepository + 实现）、`MainViewModel.cs`；Modify/Create 测试

- [ ] **Step 1: GetRecent 失败测试**（追加到现有 RepositoryTests.cs 或新文件）

```csharp
[Fact]
public void GetRecent_ReturnsAscendingLatestN()
{
    var repo = Provider.GetRequiredService<IAmountHistoryRepository>();
    var today = DateTime.Today;
    repo.UpsertToday(today.AddDays(-2), 100m, 200m);
    repo.UpsertToday(today.AddDays(-1), 110m, 220m);
    repo.UpsertToday(today, 120m, 240m);
    var rows = repo.GetRecent(2);
    Assert.Equal(2, rows.Count);
    Assert.Equal(110m * 10000 / 10000 + 220m, rows[0].Total); // 昨日在前（升序）
    Assert.Equal(today.ToString("yyyy-MM-dd"), rows[1].Date);
}
```
（Total 断言按 UpsertToday 语义调整：Total=Sh+Sz；断言 rows[1].Date=今日、rows.Count=2 即可，具体 Total 表达式以仓储实际实现为准——先读 Repositories.cs 的 UpsertToday 确认。）

- [ ] **Step 2: 实现**

```csharp
/// <summary>最近 N 个有记录的交易日（升序）。</summary>
List<DailyAmountEntity> GetRecent(int days);
```
```csharp
public List<DailyAmountEntity> GetRecent(int days)
{
    using var db = dbFactory.CreateDbContext();
    return db.DailyAmountHistory.AsNoTracking()
        .OrderByDescending(d => d.Date)
        .Take(days)
        .OrderBy(d => d.Date)
        .ToList();
}
```

- [ ] **Step 3: VM 挂接归档**（MainViewModel）

构造注入 `IKlineArchiver klineArchiver`（字段 `_klineArchiver`）；`RefreshAsync` 成功块尾部（MarketMoodPct 回填之后）追加：

```csharp
// 收盘归档：交易日 15:05 后首个刷新落库（后台执行，避免阻塞 UI；幂等可重试）
var quotes = result.Quotes;
var now = DateTime.Now;
_ = Task.Run(() => _klineArchiver.TryArchive(quotes, now));
```

- [ ] **Step 4: 测试通过 + 构建；提交** `feat: 挂接收盘归档至刷新链路，成交额仓储新增 GetRecent`

---

## Task 5: KLineChart 自绘控件

**Files:** Create `src/StockWidget.App/Views/KLineChart.cs`

- [ ] **Step 1: 控件**（完整实现）

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace StockWidget.App.Views;

/// <summary>日K蜡烛图：最近 N 根蜡烛 + 成交量副图（底部1/4）+ MA5/10/20 均线。</summary>
public sealed class KLineChart : FrameworkElement
{
    public static readonly DependencyProperty KlinesProperty = DependencyProperty.Register(
        nameof(Klines), typeof(IReadOnlyList<DailyKlineEntity>), typeof(KLineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>显示根数上限。</summary>
    public int VisibleBars { get; set; } = 120;

    public IReadOnlyList<DailyKlineEntity>? Klines
    {
        get => (IReadOnlyList<DailyKlineEntity>?)GetValue(KlinesProperty);
        set => SetValue(KlinesProperty, value);
    }

    private static Brush T(string key) => Application.Current.Resources[key] as Brush ?? Brushes.Gray;

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var all = Klines;
        if (all is not { Count: > 1 } || w <= 0 || h <= 0) return;

        var bars = all.TakeLast(Math.Min(VisibleBars, all.Count)).ToList();
        var volH = h / 4;                       // 成交量副图高度
        var priceH = h - volH - 6;
        var maxP = bars.Max(b => b.High);
        var minP = bars.Min(b => b.Low);
        if (maxP <= minP) maxP = minP + 0.0001m;
        var maxV = bars.Max(b => b.Volume);
        if (maxV <= 0) maxV = 1;

        decimal Ma(int i, int n) => i + 1 < n
            ? decimal.MinValue
            : bars.Skip(i + 1 - n).Take(n).Average(b => b.Close);

        var barW = w / bars.Count;

        // 均线（先画在蜡烛下层）：MA5 白 / MA10 黄 / MA20 紫
        DrawMa(dc, bars, 5, w, priceH, maxP, minP, Colors.White);
        DrawMa(dc, bars, 10, w, priceH, maxP, minP, Colors.Gold);
        DrawMa(dc, bars, 20, w, priceH, minP, maxP, Colors.MediumPurple);

        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var xC = (i + 0.5) * barW;
            var up = b.Close >= b.Open;
            var brush = T(up ? "UpBrush" : "DownBrush");
            var yP(double p) => priceH - (double)((decimal)p - minP) / (maxP - minP) * (priceH - 4) - 2;

            // 影线
            dc.DrawLine(new Pen(brush, 1), new Point(xC, yP((double)b.High)), new Point(xC, yP((double)b.Low)));
            // 实体（平盘画 1px 横线）
            var yO = yP((double)b.Open);
            var yC = yP((double)b.Close);
            var top = Math.Min(yO, yC);
            var bodyH = Math.Max(1, Math.Abs(yC - yO));
            var bodyW = Math.Max(1, barW * 0.6);
            dc.DrawRectangle(brush, null, new Rect(xC - bodyW / 2, top, bodyW, bodyH));

            // 成交量柱
            var vh = (double)(b.Volume / maxV) * (volH - 4);
            dc.DrawRectangle(brush, null, new Rect(xC - bodyW / 2, h - vh, bodyW, vh));
        }
    }

    private void DrawMa(DrawingContext dc, IReadOnlyList<DailyKlineEntity> bars, int n,
        double w, double priceH, decimal maxP, decimal minP, Color color)
    {
        if (bars.Count < n) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, color.R, color.G, color.B)), 1);
        pen.Freeze();
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            for (var i = n - 1; i < bars.Count; i++)
            {
                var ma = bars.Skip(i + 1 - n).Take(n).Average(b => b.Close);
                var x = (i + 0.5) * w / bars.Count;
                var y = priceH - (ma - minP) / (maxP - minP) * (priceH - 4) - 2;
                if (!started) { ctx.BeginFigure(new Point(x, y), false, false); started = true; }
                else ctx.LineTo(new Point(x, y), true, false);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    protected override Size MeasureOverride(Size availableSize) => availableSize;
}
```
（`DailyKlineEntity` 来自 `StockWidget.Core.Data.Entities`，文件头加 `using StockWidget.Core.Data.Entities;`。`DrawMa` 第三/四参数顺序笔误以实现为准：统一 `(maxP, minP)`。实现时保证可编译。）

- [ ] **Step 2: 构建 0 警告 0 错误；提交** `feat: KLineChart 自绘蜡烛图控件（蜡烛/量副图/均线）`

---

## Task 6: 分时窗口加「分时 | 日K」页签

**Files:** Modify `src/StockWidget.App/Views/MinuteChartWindow.xaml(.cs)`

- [ ] **Step 1: XAML**——标题行（Grid Row=0）之后插入页签行，内容区改为两页切换：

```xml
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,8,0,0">
            <ToggleButton x:Name="TabMinute" Content="分时" IsChecked="True" Width="60"
                          Click="TabMinute_Click" />
            <ToggleButton x:Name="TabKline" Content="日K" Width="60" Margin="6,0,0,0"
                          Click="TabKline_Click" />
            <TextBlock x:Name="RangeText" Margin="14,0,0,0" VerticalAlignment="Center"
                       FontSize="11" Foreground="{DynamicResource SubFgBrush}" />
        </StackPanel>
```
内容区：现有 Chart Border 外包一个 Grid（Row=2），新增日K页 Border（Row=3？）——**简化实现**：保留现有 Border（分时），新增同位日K Border `x:Name="KlinePanel"`（`Visibility="Collapsed"`，内含 `<views:KLineChart x:Name="KlineChart" />`），由 code-behind 切换两 Border 的 Visibility；页签行 Grid.Row 改 1、原内容 Row 顺延（RowDefinitions 增一行 Auto）。页签互斥：TabMinute.IsChecked / TabKline.IsChecked 手动切换。

- [ ] **Step 2: code-behind**（核心逻辑）

```csharp
private bool _showingKline;

private void TabMinute_Click(object sender, RoutedEventArgs e)
{
    _showingKline = false;
    TabMinute.IsChecked = true; TabKline.IsChecked = false;
    MinutePanel.Visibility = Visibility.Visible;   // 现有分时 Border 命名 MinutePanel
    KlinePanel.Visibility = Visibility.Collapsed;
    RangeText.Text = "";
}

private void TabKline_Click(object sender, RoutedEventArgs e)
{
    _showingKline = true;
    TabKline.IsChecked = true; TabMinute.IsChecked = false;
    MinutePanel.Visibility = Visibility.Collapsed;
    KlinePanel.Visibility = Visibility.Visible;
    _ = LoadKlinesAsync();
}

/// <summary>加载日K并计算区间涨跌标签（后台查询，回 UI 渲染）。</summary>
private async Task LoadKlinesAsync()
{
    var code = _row.Code;
    var rows = await Task.Run(() => _vm.GetKlines(code, 250));
    KlineChart.Klines = rows;
    RangeText.Text = BuildRangeText(rows);
}

/// <summary>近 5/20/60/250 日涨跌幅（根数不足显示 —）。涨跌 = 最新收盘 / N日前收盘 - 1。</summary>
private static string BuildRangeText(IReadOnlyList<DailyKlineEntity> rows)
{
    string Part(int n)
    {
        if (rows.Count <= n) return $"近{n}日：—";
        var chg = (rows[^1].Close / rows[^(n + 1)].Close - 1m) * 100m;
        return $"近{n}日：{chg:+0.##;-0.##}%";
    }
    return string.Join("  ", Part(5), Part(20), Part(60), Part(250));
}
```

- [ ] **Step 3: VM 侧数据入口**（MainViewModel 新增，后台查询由窗口 Task.Run 承担）

```csharp
/// <summary>取某代码最近 N 根日K（供 K 线页）。</summary>
public List<DailyKlineEntity> GetKlines(string code, int days) =>
    _klineRepo.GetByCode(code, days);
```
（构造注入 `IDailyKlineRepository _klineRepo`。）

- [ ] **Step 4: 构建 + 提交** `feat: 分时窗口分时/日K页签与区间涨跌标签`

---

## Task 7: 成交额趋势窗口

**Files:** Create `src/StockWidget.App/Views/AmountTrendChart.cs`、`src/StockWidget.App/Views/AmountTrendWindow.xaml(.cs)`；Modify `MainViewModel.cs`、`MainWindow.xaml.cs`

- [ ] **Step 1: AmountTrendChart**（依赖属性 `Amounts`（升序 decimal）、`DrawAmount`：60 柱红绿（较前日）+ MA5/20 折线；绘制逻辑同 KLineChart 风格——柱宽 = w/Count，量程 0..max；MA 折线跳过不足段）

- [ ] **Step 2: AmountTrendWindow**（GlassWindow，Width=640 Height=420 CenterOwner 单实例；标题行：总量/较昨/近5日均值/近20日均值/今日排名；中部 AmountTrendChart；cs：`LoadAsync()` → `Task.Run(_vm.GetRecentAmounts(60))` → 赋 Amounts + 统计文本；Esc 关闭）

- [ ] **Step 3: VM**（构造注入 `IAmountHistoryRepository`（已有 `_amountHistoryRepo`——确认字段名）；新增：

```csharp
public event Action? AmountTrendRequested;
public List<DailyAmountEntity> GetRecentAmounts(int days) => _amountHistoryRepo.GetRecent(days);
public void RequestAmountTrend() => AmountTrendRequested?.Invoke();
```

- [ ] **Step 4: MainWindow 接线**（单实例 `_amountTrendWindow`，模式同 `_minuteWindow`）；右键菜单在"切换主题"前插入 `📈 成交额趋势`（`Click → _vm.RequestAmountTrend()`）；`AmountText_Click` 中 `ClickCount >= 2` 分支改调 `_vm.RequestAmountTrend()`（原 ToggleVisibility 移除——表头双击仍显隐）

- [ ] **Step 5: 构建 0 警告 0 错误 + 69 测试；提交** `feat: 成交额趋势窗口（60日柱状+均线+统计）`

---

## Task 8: 收尾验证

- [ ] **Step 1: 全量** Debug/Release 双构建 0 警告 0 错误；69+新增测试全过
- [ ] **Step 2: CHANGELOG** Unreleased 段记录（日K/趋势/归档）
- [ ] **Step 3: 提交 CHANGELOG**
- [ ] **Step 4: 运行冒烟**（15:05 后验证归档表有数据、页签切换、趋势窗口）

---

## Self-Review

- **Spec 覆盖**：daily_kline 表(T1)、归档(T3/T4)、KLineChart(T5)、页签(T6)、趋势窗口+入口(T7)、GetRecent(T4)、测试(T2/T3/T4) — 全覆盖。
- **类型一致**：`IDailyKlineRepository.UpsertRange/GetArchivedCodesToday/GetByCode`、`IKlineArchiver.TryArchive(quotes, now)`、`DailyKlineEntity`、`GetRecent(days)` 各任务一致。
- **占位符**：Task 4 Step 1 的 Total 断言标注"以 UpsertToday 实际语义为准"——属实现前必读指令而非占位；KLineChart DrawMa 参数顺序笔误已标注以可编译为准。无 TBD。