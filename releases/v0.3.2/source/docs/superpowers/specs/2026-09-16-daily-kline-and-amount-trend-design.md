# 设计文档：日K归档 + K线图 + 成交额趋势

> 日期：2026-09-16 ｜ 项目：股票小插件（StockWidget） ｜ 目标版本：v0.2.0（MINOR：新功能）
> 基线：v0.1.2（9963bd4）

---

## 1. 目标

基于 SQLite 已沉淀的历史数据，新增两个历史行情功能：

1. **日K归档 + K线图**：每个交易日收盘后自动归档全自选 OHLC，积累真实历史行情；分时窗口内切换查看 K 线（蜡烛 + 成交量 + 均线 + 区间涨跌）。
2. **成交额趋势图**：基于 `daily_amount_history` 已有 2 年数据，可视化近 60 日沪深成交额（柱状 + 均线 + 高低统计）。

用户已确认的入口决策：日K 归档用 15:05 后自动抓取（零额外请求）；K 线图入口为分时窗口页签；成交额趋势入口为右键菜单 + 双击底部量能栏。

---

## 2. 日K数据层（Core）

### 2.1 新表 daily_kline

| 列 | 类型 | 说明 |
| --- | --- | --- |
| Code | string | 联合主键，规范化代码（sh600390） |
| Date | string | 联合主键，yyyy-MM-dd |
| Open / High / Low / Close | decimal | 当日开高低收 |
| Volume | decimal | 成交量（手，腾讯口径） |
| Amount | decimal | 成交额（万元，腾讯口径） |

实体 `DailyKlineEntity`；DbContext 新增 DbSet + 映射；EF 迁移 `AddDailyKline`。

### 2.2 归档服务 KlineArchiver

**触发点**：`MainViewModel.RefreshAsync` 成功块尾部调用 `KlineArchiver.TryArchive(result.Quotes)`——**零额外请求**（刷新本身已抓全自选 OHLC）。

**归档条件（同时满足）**：
- `ITradingCalendar.IsTodayTrading()`（交易日）
- 本地时间 ≥ **15:05**
- 当日尚未归档：`GetArchivedCodesToday()` 一次查询做差，仅写入缺失代码（幂等，支持跨日补归档）

**行为**：满足 → Quotes 中 `Success` 的全部条目 upsert 进 `daily_kline`；不满足静默跳过；写库失败静默（下个 tick 重试）。

**数据积累说明**：K 线从启用日起从零积累；历史无法回填（旧数据仅沪深总量）。

### 2.3 仓储 IDailyKlineRepository

- `UpsertRange(IEnumerable<DailyKlineEntity>)`
- `HashSet<string> GetArchivedCodesToday()`（今日已归档的代码集合，供归档前差集判断）
- `List<DailyKlineEntity> GetByCode(string code, int days)`（按日期倒序取最近 N 根，返回升序）

DI 注册；`IAmountHistoryRepository` 新增 `List<DailyAmountEntity> GetRecent(int days)`（按日期倒序取最近 N 天，返回升序）。

---

## 3. K线图 UI

### 3.1 入口

`MinuteChartWindow` 顶部标题行下方加 **「分时 | 日K」切换**（两个 ToggleButton 或 TabControl），默认分时。切换不重新开窗。

### 3.2 KLineChart 自绘控件（新，App/Views/KLineChart.cs）

`FrameworkElement` + DrawingContext（与 Sparkline 同风格），依赖属性 `Klines`（IReadOnlyList<DailyKline>）：

- **显示范围**：最近 120 根（MVP 不做拖动缩放）
- **蜡烛**：红涨绿跌（UpBrush #FF0000 / DownBrush #008000），影线 + 实体
- **成交量副图**：控件底部 1/4 高度，红绿柱（按当日涨跌着色）
- **均线**：MA5 / MA10 / MA20（叠加在蜡烛区；颜色白 / 黄 / 紫，浅色主题用对应深色）
- **昨收基准虚线**：BaselineBrush
- **顶部区间涨跌标签**（由窗口侧文本显示，不画进控件）：近 5 / 20 / 60 / 250 日涨跌幅（`Close/前收 - 1`；根数不足显示"—"）

### 3.3 窗口布局

MinuteChartWindow：标题行不变；页签下为内容区（分时页 = 现有 Sparkline + BaselineBar；日K页 = KLineChart + 区间涨跌标签行）。日K数据在切换到日K页时异步加载（`GetByCode(code, 250)`），不阻塞 UI。

---

## 4. 成交额趋势 UI

### 4.1 入口

- 右键菜单新增「📈 成交额趋势」
- **双击底部量能栏**打开趋势窗口（原底栏双击为显隐窗口——显隐保留在表头双击，不受影响）

### 4.2 AmountTrendWindow + AmountTrendChart

- 新窗口（GlassWindow，约 640×400，单实例复用同分时窗口模式）
- `AmountTrendChart : FrameworkElement`，依赖属性 `Amounts`（升序 decimal 列表）：
  - **近 60 个交易日柱状图**：红 = 较前一交易日放量、绿 = 缩量
  - **5 日 / 20 日均值折线**
- 顶部标签：今日总量、较昨增减（沿用主窗口口径）、近 5 / 20 日均值、**"今日为近 60 日第 N 高"**
- 数据：`GetRecent(60)` 异步加载

---

## 5. 影响面

**Core 新增/修改**：
- `DailyKlineEntity` + DbContext 映射 + EF 迁移
- `IDailyKlineRepository / DailyKlineRepository`
- `KlineArchiver`（归档服务）
- `IAmountHistoryRepository.GetRecent`
- `ServiceCollectionExtensions` 注册
- `PriceRefreshService` 或 VM 侧挂接归档调用（放 VM 刷新成功块，保持 RefreshService 纯粹）

**App 新增/修改**：
- `KLineChart.cs` / `AmountTrendChart.cs`（自绘）
- `MinuteChartWindow.xaml(.cs)`（页签 + 日K页 + 区间标签）
- `AmountTrendWindow.xaml(.cs)`（新窗口 + 单实例）
- `MainWindow.xaml.cs`（右键菜单项 + 底栏双击改趋势窗口）
- `MainViewModel.cs`（归档调用挂接 + 打开趋势窗口事件）

**测试**：
- `KlineArchiverTests`：归档条件（非交易日 / 早于15:05 / 已归档跳过；正常 upsert；补归档）
- `DailyKlineRepositoryTests`：UpsertRange 幂等、GetByCode 排序
- `GetRecent` 测试

---

## 6. 边界 / 不做

- K 线 MVP 不做拖动缩放、十字光标（后续版本可加）
- 历史日K无法回填，从启用日起积累
- 不引入第三方图表库（延续自绘风格）
- 成交额趋势仅沪深两市总量（daily_amount_history 现有口径），不含个股成交额趋势（个股看日K成交量副图）

---

## 7. 验证

```
dotnet build StockWidget.slnx -c Debug && dotnet test StockWidget.slnx
运行冒烟（交易时段 / 15:05 后）：
  - 15:05 后首个刷新写入 daily_kline（二次刷新不重复写）
  - 双击行 → 分时 | 日K 页签切换，蜡烛/均线/成交量/区间标签正确
  - 右键 / 双击底栏 → 成交额趋势窗口，柱状/均线/统计正确
  - 主题深浅切换下图表配色正常
```
