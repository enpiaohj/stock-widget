# 设计文档：StockWidget v1.1 完善计划

> 日期：2026-09-15 ｜ 状态：草案 ｜ 项目：股票小插件（StockWidget）
> 目标：修复明确缺陷 + 节假日休市识别 + 按名称/拼音添加股票。

基线：当前 `main`（v1.0.0 已发布）。参考旧版 Python 源码 `D:\AIProjects\stockTool` 与 C# 现有实现。

---

## 1. 背景与目标

C# / WPF（.NET 10）重写版 v1.0.0 已对齐并增强 Python 旧版 P6 UI v1.0.3.10。
本次在 v1.0.0 层基础上做三个增量，全部为用户已确认方向：

1. **修复明确缺陷**（低风险、可感）：
   - 删除模板残留 `src/StockWidget.Core/Class1.cs`（空类，无任何引用）。
   - 接通"托盘图标大盘涨跌色点"：README/CHANGELOG 声称已实现，实际启动时
     `App.xaml.cs` 传 `TrayIconFactory.Create(default, showOverlay: false)` 写死无色点，
     且全程无行情刷新更新托盘图标 —— 功能名存实亡。
2. **节假日休市识别**（内置交易日历表 + SQLite 存储，智能抓取）。
3. **按名称/拼音加股**（腾讯 smartbox 搜索 + 输入框下拉联动实时查询）。

---

## 2. 修复明确缺陷

### 2.1 删除 Class1.cs

`src/StockWidget.Core/Class1.cs` 为类库模板生成的空类，`grep` 确认无引用。
直接删除文件，无需其他改动。

### 2.2 接通托盘大盘涨跌色点

**现状**：
- `TrayIconFactory.Create(Color overlayColor, bool showOverlay)` 已实现色点绘制逻辑
  （App 层，`Services/AppServices.cs`），但只有 `App.xaml.cs` 在启动时调用一次且传入
  `showOverlay: false`，此后再无更新——托盘图标恒定无颜色。

**目标**：每次行情刷新后，依据大盘指数当日涨跌更新托盘 icon 的右上角色点
（当前色点已默认「红=涨、绿=跌」配色），涨=红点、跌=绿点、平/无数据=灰点。

**改动设计**：
- `MainViewModel`（Core 无关，App 层 VM）在 `RefreshAsync` 完成后，取自选股中
  上证指数 `sh000001` 的 `ChangePct`（若自选无该指数，则跳过，不更新色点）：
  - 新增公开可观察属性 `public decimal? MarketMoodPct { get; private set; }`，并在刷新回填。
- `MainWindow` 订阅 `PropertyChanged` 中 `MarketMoodPct` 变更，回调 `App.Current.UpdateTrayMood(pct)`。
- `App` 新增公开方法 `UpdateTrayMood(decimal? pct)`：
  ```csharp
  _tray.IconSource = TrayIconFactory.Create(
      pct is null ? Colors.Gray
    : pct > 0 ? Colors.Red
    : Colors.Green,
      showOverlay: true);
  ```
  启动首次调用改为 `showOverlay: false`（初始无数据，不亮点色），首个刷新生效。

**边界**：
- 无 `sh000001` 在自选时 → 不更新色点（保持现有图标），不强制。
- 网络失败保留旧值 → 色点沿用上次，不闪烁。

---

## 3. 节假日休市识别（内置交易日历 + 智能抓取）

### 3.1 数据模型

新增表 `trading_calendar`：

| 列 | 类型 | 说明 |
| --- | --- | --- |
| Date | string `yyyy-MM-dd` | 主键，交易日历日期 |
| IsTradingDay | bool | true=交易日（含调休上班的周末）；false=休市日（法定节假日） |
| Remark | string | 备注（如"春节假期""调休上班"） |

领域模型 `StockWidget.Core/Data/Entities/Entities.cs` 新增 `TradingCalendarEntity`；
DbContext 新增 `DbSet<TradingCalendarEntity>`，EF Migration 生成。

### 3.2 内置种子数据

`StockWidget.Core/Services/TradingCalendarSeeder.cs`：
- 内置一段 `Dictionary<DateOnly,bool>`，覆盖 **2025–2028 年中国法定节假日及调休上班日**，
  以准确识别节假日（休市）与调休上班的周末（开盘）。
- 提供 `SeedIfEmpty(StockWidgetDbContext)`：库中该表为空时插入内置数据，非覆盖（保证可维护性）。
- 数据来源：国务院办公厅历年放假安排公告（节假日统一休市；调休上班的周末为交易日）。

### 3.3 判定服务

`StockWidget.Core/Services/TradingCalendarService.cs`，接口 `ITradingCalendar`：
```csharp
public interface ITradingCalendar
{
    /// <summary>指定日是否为交易日（非交易日 → 休市）。</summary>
    bool IsTradingDay(DateTime date);
    /// <summary>今天是交易日吗（缓存按小时）。</summary>
    bool IsTodayTrading();
}
```
判定逻辑：
- 命中 `trading_calendar`（含未来年份种子或用户维护）→ 按表返回。
- 未命中（如 2029+ 未被种子覆盖）→ 默认按「周末休市、工作日开盘」兜底，
  保证永远有结果，不抛异常。

### 3.4 智能抓取

重构 `MainViewModel.RefreshAsync` 顶部判定：
- 当前逻辑 `IsMarketClosed(now)` 仅判断周末；改为优先 `ITradingCalendar.IsTodayTrading()`，
  失败兜底周末规则。
- 进入休市（非交易日）后：
  - **跳过行情/量能/分时抓取**（不请求网络），避免节假日无效高频抓取（省流量、省电量）。
  - 状态栏显示休市徽章，`MarketClosed=true` 徽章显示（已具备可用判定）。
  - **临时将 `_refreshTimer.Interval` 拉长到探测间隔**（如 600 秒），每个 tick 再次判定；
    一旦检测到进入交易日（如节后首个工作日开盘），恢复配置的刷新间隔并正常抓取。
- 交易日行为与现状完全一致（正常间隔抓取）。

### 3.5 配置/可维护入口

沿用现有 SQLite（EF Core）模式，种子可被用户后续增删。
由于本项目尚无对外管理 UI，本次仅提供种子 + 服务，不新增管理界面（YAGNI）；
后续版本可扩展设置中心「交易日历」页。

---

## 4. 按名称/拼音添加股票（腾讯 smartbox 搜索 + 下拉联动）

### 4.1 搜索数据源

扩充 `ITencentQuoteApi`（`Services/TencentQuoteApi.cs`）新增适配器方法：
```csharp
/// <summary>腾讯智能搜索 smbox（smartbox.gtimg.cn，GBK）。</summary>
Task<IReadOnlyList<StockSearchMatch>> SearchSuggestAsync(string keyword, CancellationToken ct = default);
```
领域模型 `StockWidget.Core/Models/StockSearchMatch.cs`：
```csharp
public sealed record StockSearchMatch(string Code, string Name, string Market);
```
- `Code` 为已归一化代码（复用 `StockCodeNormalizer`，如 `sh600390`）。
- `Market`：A 股（沪/深）／港股／美股（按返回市场字段映射，保证 `Normalize` 兼容）。
- 请求 `https://smartbox.gtimg.cn/s3/?v=2&q={kw}&t=all`，GBK 解码
  （沿用现有 `Encoding.GetEncoding("GBK")` 方式）。
- 解析封装为可测静态 `SmartboxResponseParser.Parse`（与现有 `TencentResponseParser` 同风格），
  支持中文、拼音、代码关键字。网络异常返回空列表（不抛出，UI 兜底）。

### 4.2 UI 交互（输入下拉联动）

在现有「添加股票」流程的输入框上做下拉联动，选择后直接添加：

- 复用 `InputDialog`，新增可选构造函数参数/方法：
  ```csharp
  public InputDialog(string title, string prompt, string? hint, Func<string, Task<IReadOnlyList<StockSearchMatch>>>? searchSource)
  ```
- `InputDialog.xaml` 在输入框下方新增候选下拉 `ListBox`（默认隐藏），
  高度受限并支持滚动；触发时机：
  - 输入内容 ≥ 1 个字符、且 `searchSource` 非空时启用搜索。
  - **防抖 300ms**，异步调用 `searchSource`，取消上次未完成请求（`CancellationTokenSource`）。
  - 返回空 → 隐藏下拉。
  - 键盘：`↑`/`↓` 移动高亮，`Enter` 选中高亮候选（无高亮时提交输入框原文本，保持现状），
    `Esc` 关闭下拉/退出。
  - 鼠标点击候选 → 选中即添加。
- 选中候选后 `MainWindow` 的添加逻辑改为 `_vm.AddStockAsync(match.Code)`（复用现有校验/插入/刷新）。

### 4.3 交互边界

- 搜索失败 / 无网 → 静默空下拉，用户仍可直接输入代码提交（不阻断原有流程）。
- 选中下拉项后下拉关闭；再次输入自动重新搜索。
- 支持同时展示多市场结果（如 deepseek 同时匹配 A 股/港股/美股），用户自选。

---

## 5. 影响面与文件

**新增**：
- `StockWidget.Core/Data/Entities/Entities.cs`（追加 `TradingCalendarEntity`）
- `StockWidget.Core/Services/TradingCalendarService.cs` + `ITradingCalendar`
- `StockWidget.Core/Services/TradingCalendarSeeder.cs`（内置 2025–2028 日历）
- `StockWidget.Core/Services/TradingCalendarSeeder.cs` 种子数据文件（可并入 Seeder）
- `StockWidget.Core/Models/StockSearchMatch.cs`
- `StockWidget.Core/Services/SmartboxResponseParser.cs`（可并入 TencentQuoteApi）
- EF Migration（`trading_calendar` 表）

**修改**：
- `StockWidget.Core/Data/StockWidgetDbContext.cs`（`DbSet<TradingCalendarEntity>` + 映射）
- `StockWidget.Core/Services/TencentQuoteApi.cs`（`SearchSuggestAsync` 实现 + 接口）
- `StockWidget.Core/ServiceCollectionExtensions.cs`（注册 `ITradingCalendar`）
- `StockWidget.App/ViewModels/MainViewModel.cs`（休市智能抓取 + `MarketMoodPct`）
- `StockWidget.App/Views/MainWindow.xaml.cs`（托盘色点订阅 + 添加股票走搜索流程）
- `StockWidget.App/App.xaml.cs`（`UpdateTrayMood` + 首次 `showOverlay` 修正）
- `StockWidget.App/Views/InputDialog.xaml(.cs)`（可选搜索下拉联动）
- 删除：`src/StockWidget.Core/Class1.cs`

**测试新增**：
- `TradingCalendarServiceTests`：法定节假日/调休上班周日/普通周末/普通工作日判定，
  未命中兜底逻辑。
- `SmartboxResponseParserTests`：中文/拼音/代码关键字解析，含多市场、异常输入。
- `TencentResponseParserTests` 风格统一（沿用现有 xUnit + 纯静态解析测试）。

---

## 6. 已知边界 / 不做

- **不新增交易日历管理 UI**：本次仅内置种子 + 服务，管理页后续版本做（YAGNI）。
- **节假日识别精度取决于内置种子表**：种子覆盖至 2028；之后年份未命中按周末兜底。
- **名称搜索为提示性**：腾讯 smartbox 为公开接口，字段/响应可能变化，
  解析失败时静默回退为直接代码输入，不影响核心流程。
- **托盘色点仅在有 `sh000001` 自选时生效**：无该指数则保持原图标。
- 不进行无关重构，遵循最小必要改动。

---

## 7. 验证

```
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
运行 dotnet run --project src/StockWidget.App 冒烟：
  - 托盘图标随大盘涨跌变色
  - 休市日状态栏显示休市徽章且不抓取
  - 添加股票下拉搜索可选并成功添加
```
> 运行时验证依赖真实联网与交易日；无法联业/非交易日项目如实标注"未执行"。