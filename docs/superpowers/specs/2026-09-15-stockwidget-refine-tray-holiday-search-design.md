# 设计文档：StockWidget v1.1 完善计划

> 日期：2026-09-15 ｜ 状态：草案 ｜ 项目：股票小插件（StockWidget）
> 目标：修复明确缺陷 + 节假日休市识别 + 按名称/拼音添加股票 + UI 深度优化。

基线：当前 `main`（v1.0.0 已发布）。参考旧版 Python 源码（项目 stockTool）与 C# 现有实现。

---

## 1. 背景与目标

C# / WPF（.NET 10）重写版 v1.0.0 已对齐并增强 Python 旧版 P6 UI v1.0.3.10。
本次在 v1.0.0 层基础上做四块，全部为用户已确认方向：

1. **修复明确缺陷**（低风险、可感）：
   - 删除模板残留 `src/StockWidget.Core/Class1.cs`（空类，无任何引用）。
   - 接通"托盘图标大盘涨跌色点"：README/CHANGELOG 声称已实现，实际启动时
     `App.xaml.cs` 传 `TrayIconFactory.Create(default, showOverlay: false)` 写死无色点，
     且全程无行情刷新更新托盘图标 —— 功能名存实亡。
2. **节假日休市识别**（内置交易日历表 + SQLite 存储，智能抓取）。
3. **按名称/拼音加股**（腾讯 smartbox 搜索 + 输入框下拉联动实时查询）。
4. **UI 深度优化**：在现有玻璃拟态自绘卡片架构上做**拟质感拉满**
   （用户已确认，不启用真亚克力 DWM，保持 Win10/11 一致、零兼容风险）。

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

**新增（Core/业务）**：
- `StockWidget.Core/Data/Entities/Entities.cs`（追加 `TradingCalendarEntity`）
- `StockWidget.Core/Services/TradingCalendarService.cs` + `ITradingCalendar`
- `StockWidget.Core/Services/TradingCalendarSeeder.cs`（内置 2025–2028 日历）
- `StockWidget.Core/Models/StockSearchMatch.cs`
- `StockWidget.Core/Services/SmartboxResponseParser.cs`（可并入 TencentQuoteApi）
- EF Migration（`trading_calendar` 表）

**新增（UI）**：
- `StockWidget.App/Services/AccentPalettes.cs`（强调色板定义：5 组 + 派生刷）
- `StockWidget.App/Themes/AccentThemeManager.cs` 或并入 `ThemeManager`（按强调色派生扁平刷资源）

**修改（Core/业务）**：
- `StockWidget.Core/Data/StockWidgetDbContext.cs`（`DbSet<TradingCalendarEntity>` + 映射）
- `StockWidget.Core/Services/TencentQuoteApi.cs`（`SearchSuggestAsync` 实现 + 接口）
- `StockWidget.Core/ServiceCollectionExtensions.cs`（注册 `ITradingCalendar`）
- `StockWidget.Core/Models/AppSettings.cs`（新增 `AccentColor` 设置项）
- `StockWidget.App/ViewModels/MainViewModel.cs`（休市智能抓取 + `MarketMoodPct` + 强调色应用）
- `StockWidget.App/Views/MainWindow.xaml.cs`（托盘色点订阅 + 添加股票走搜索流程 + UI 交互）
- `StockWidget.App/App.xaml.cs`（`UpdateTrayMood` + 首次 `showOverlay` 修正）
- `StockWidget.App/Views/InputDialog.xaml(.cs)`（可选搜索下拉联动）
- 删除：`src/StockWidget.Core/Class1.cs`

**修改（UI 文件）**：
- `MainWindow.xaml`（卡片渐变底、双描边、阴影、锁定徽章、分组头/表头/量能栏细节）
- `Themes/Dark.xaml` / `Light.xaml`（深化配色 + 新增 `CardGradientBrush`/`CardShadowBrush`/
  `ChipBrush`/`HoverStrokeBrush` 等资源；强调色派生支持）
- `Controls.xaml`（表头排序箭头、分组头、量能文字等新样式/触发器）
- `Views/Sparkline.cs`（面积渐变 + 末点光标）
- `Views/MinuteChartWindow.xaml`（价格 TabularFigures、基准虚线、当前价横线）
- `Views/SettingsWindow.xaml(.cs)`（「通用」页强调色选择）
- `Views/AboutWindow.xaml(.cs)`（版本/图标/链接排版）
- `ThemeManager.cs`（强调色派生效 + 跟随系统联动）

**测试新增**：
- `TradingCalendarServiceTests`：法定节假日/调休上班周日/普通周末/普通工作日判定，
  未命中兜底逻辑。
- `SmartboxResponseParserTests`：中文/拼音/代码关键字解析，含多市场、异常输入。
- `AccentPaletteTests`（如抽成纯逻辑）：强调色派生刷、可选色板有效性。
- 既有 `TencentResponseParserTests` 风格统一（沿用现有 xUnit + 纯静态解析测试）。

---

## 6. UI 深度优化（拟质感拉满）

现有 UI 已是统一的玻璃拟态卡片：透明窗口 + 自绘圆角卡片、深/浅/跟随系统三态主题、
动态资源热切换、红涨绿跌、橙色强调、行闪烁动效。本次在**不改变自绘架构**的前提下，
从「视觉细节」「主题与调色板」「窗口体验层」「分时/设置/关于细节」四方面做**拟质感拉满**，
Win10/11 一致，零 DWM 兼容风险。

### 6.1 视觉细节打磨（高响应路）

- **卡片层次**：主卡片背景由纯色 `BgBrush` 改为「径向渐变 + 半透明叠加」，玻璃质感更透气；
  用 `#26FFFFFF` 顶部高光叠加制造明暗层次（GradientStop 方案，保持 DynamicResource 可切主题）。
  - 做法：`MainWindow.xaml` 的 Border Background 由 `{DynamicResource BgBrush}`
    改为 `{DynamicResource CardGradientBrush}`（LinearGradientBrush，两组停止色，深浅主题各一套）。
- **边框与阴影**：主卡 1px 外框 + 内 1px 高光描边（双 Border 叠加），圆角 12px 保持；
  启用 `DropShadowEffect`（BlurRadius≈20，Opacity≈0.35，色随主题）替代 Win 默认轻微阴影，
  悬浮窗更显质感。
- **行内迷你走势**：`Sparkline` 增加面积渐变填充（折线下淡色渐隐 + 顶部描边），
  涨/跌使用对应 `UpBrush`/`DownBrush`，区分度更高；末点加圆点光标。
- **分组头**：由纯色块改为「渐隐底 + 左竖条」样式，与卡片语言统一，分组更醒目不抢戏。
- **表头**：排序激活时表头文字加粗 + 右上加排序箭头（▲/▼），当前表头仅有 hover、无排序视觉反馈。
- **量能栏**：金额数字使用等宽变体（`TabularFigures`）避免刷新抖动；增减段保持红/绿加粗。
- **过渡动效**：窗口 Show/Hide 在显隐时加轻微淡入（Opacity 0→目标 100ms），
  ToggleVisibility 由直接 Show/Hide 改为 100–150ms 淡入/淡出（线程安全 Dispatcher 动画），
  连续刷新不打断。

> 注意：`AllowsTransparency=true` 下 WPF 的 `Opacity`/`DropShadowEffect` 正常生效，
> 阴影需设定在最高层 Border 上而非 Window 本身，避免绘制阴影到透明区域被裁切。

### 6.2 主题与调色板

- **三态主题深/浅各自深化**：
  - 深色：底色从 `#23232A` 微向冷灰中性偏移，提升可读性；增加 `CardGradientBrush`、
    `CardShadowBrush`、`ChipBrush`（标签底）、`HoverStrokeBrush` 等新资源。
  - 浅色：加统一的暖灰背景、加深表头下划线、卡片阴影更轻。
- **可选中强调色**（新增设置项 `AccentColor`，默认沿用当前橙 `#FFA640`）：
  - 提供 5 组可选强调色：默认橙、蓝 `#38BDF8`、绿 `#22C55E`、紫 `#A78BFA`、红 `#F43F5E`。
  - 主题字典中所有强调相关色（AccentBrush / AccentSoftBrush / HeaderLineBrush / GroupHeaderBrush /
    MenuHoverBrush / FlashUp…）改为由「强调色 + 固定 alpha」派生，选中强调色后整套配色联动刷新。
  - 设置中心「通用」页新增强调色选择（单选色块组），保存后热切换。
- **跟随系统**：`system` 主题在深/浅字典间切换已实现，补上强调色随主题字典一并刷新的联动。

### 6.3 窗口体验层

- **主窗口无边框增强**：保留透明自绘，但补充：
  - 右上角叠加极简「锁定」状态小徽章（`{DynamicResource LockIconBrush}`），锁定用锁形、解锁用开锁形，
    悬停提示当前状态；复用现有 `ShowLockedStatus` 设置。
  - 窗口底部拖拽时透明度实时反馈已具备（GlassWindow 拖动 + 右键）；窗口显隐用 6.1 淡入淡出衔接。
- **托盘到主窗口**：托盘「显示窗口」调用同一 `ToggleVisibility`（带淡入），统一体验。
- **关于窗口**：补产品名/版本/© 信息排版、GitHub 仓库链接文案、图标展示区，视觉对齐设置中心卡片。

### 6.4 分时图与设置中心细节

- **分时图**：标题栏价格用 TabularFigures 避免跳动；分时图曲线（`Sparkline`）复用 6.1 的面积渐变，
  增加昨收基准虚线、当前价横线；`ClosedBadge` 与主窗口休市徽章一致。
- **设置中心**：卡片间间距与分组进一步完善（已是 CardStyle）；预警列表行 hover、Tab 切换轻微过渡；
  强调色选择区在「通用」Tab 落地。

### 6.5 阻止 / 边界

- **不启用真亚克力 DWM**（用户已确认）：保持 `AllowsTransparency=true` + 自绘圆角卡片，
  Win10/11 视觉一致、无兼容与性能风险。`WindowChrome` 维持现状。
- **透明窗口阴影**：若 `DropShadowEffect` 在某些 Win10 合成下边界发虚，回退为「细边框 + 柔和投影」，
  不影响主题与配色（实现时按实际渲染取舍）。
- 强调色为**界面色**，不改数据语义色（红涨/绿跌保持，不随强调色变）。

---

## 7. 已知边界 / 不做

- **不新增交易日历管理 UI**：本次仅内置种子 + 服务，管理页后续版本做（YAGNI）。
- **节假日识别精度取决于内置种子表**：种子覆盖至 2028；之后年份未命中按周末兜底。
- **名称搜索为提示性**：腾讯 smartbox 为公开接口，字段/响应可能变化，
  解析失败时静默回退为直接代码输入，不影响核心流程。
- **托盘色点仅在有 `sh000001` 自选时生效**：无该指数则保持原图标。
- **不启用真亚克力 DWM**：毛玻璃以拟质感拉满实现（见 §6.5），保持零兼容风险。
- **强调色不改数据涨跌语义色**：红涨绿跌恒为 UpBrush/DownBrush，不随强调色变化。
- 不进行无关重构，遵循最小必要改动。

---

## 8. 验证

```
dotnet build StockWidget.slnx -c Debug
dotnet test StockWidget.slnx
运行 dotnet run --project src/StockWidget.App 冒烟：
  - 托盘图标随大盘涨跌变色
  - 休市日状态栏显示休市徽章且不抓取
  - 添加股票下拉搜索可选并成功添加
  - 深/浅/跟随系统三态主题切换，强调色切换后整套配色联动
  - 主窗口（含锁定徽章/量能栏/分组头/走势渐变）在 Win10 与 Win11 下渲染一致
  - 窗口显隐淡入淡出流畅不卡顿
```
> 运行时验证依赖真实联网与交易日；无法联网/非交易日项目如实标注"未执行"。
> 运行时验证依赖真实联网与交易日；无法联业/非交易日项目如实标注"未执行"。