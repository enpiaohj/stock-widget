# 股票小插件 (StockWidget)

Windows 桌面股票行情悬浮窗小工具。C# / WPF (.NET 10) 全新重写版，前身是 Python/PySide6 单文件版本（旧版项目 stockTool）。

## 产品简介

常驻桌面的半透明行情悬浮窗：实时显示自选股（指数 / ETF / 个股 / 港股 / 美股）行情，统计沪深两市总成交额并与上一交易日对比增量 / 缩量；双击标的打开分时 / 日K详情窗口（同花顺风格十字光标、极值标注、AI 行情分析）；支持全局热键一键显隐、窗口锁定、托盘常驻，数据存储于本地 SQLite 数据库。

## 核心功能

- **实时行情**：腾讯行情接口（`qt.gtimg.cn`），5 秒级自动刷新（1–120 秒可配），12 个字段（名称 / 现价 / 涨跌幅 / 成交量 / 成交额 / 换手率 / 最高 / 最低 / 开盘 / 昨收 / 市值 / 振幅）自由取舍
- **分时 / 日K详情**：双击标的打开——同花顺风格分时图（241 分钟槽位、昨收基准虚线、分钟量能柱）与日K蜡烛图（MA5 / 10 / 20、成交量副图、可见区间最高 / 最低价标注、十字光标 + 右侧实时价格标签、悬停 OHLC 数据面板）
- **日K历史自动归档**：交易日 15:05 后首个刷新自动落库全自选 OHLC（零额外请求、幂等可补档）
- **日K历史回补**：启动后自动检测缺口，经腾讯历史日K接口（前复权 320 根）后台串行补齐——只插缺失不覆盖、稳态零请求、失败静默重试；底部状态栏显示回补进度
- **市场分组**：指数 / ETF / 个股 / 港股 / 美股自动分组，全宽细色条区段组头（固定市场分类色 + 标的数量），分组颜色可在设置中自定义（预设色板 + 恢复默认）
- **AI 行情分析**：DeepSeek 驱动的 K 线解读侧面板（综合判断 / 趋势结构 / 量价关系 / 压力支撑 / 风险观察），API Key DPAPI 加密存储，支持获取模型列表、测试连接、结果缓存
- **成交额趋势**：近 60 日沪深成交额柱状 + 5 / 20 日均线 + 60 日排名统计（右键菜单 / 双击量能栏打开）
- **量能统计**：沪深两市总成交额（亿），较上一交易日增量（红）/ 缩量（绿），历史入库可回看
- **悬浮窗体验**：无边框圆角、置顶 / 取消置顶、透明度调节、位置锁定、表头拖拽移动、双击表头显隐、内容自适应尺寸
- **全局热键**：`RegisterHotKey` 实现，任意组合可配，默认 `Ctrl+Q`
- **系统托盘**：双击显隐、窗口置顶 / 取消、初始化位置、退出；图标叠加大盘涨跌色点
- **本地数据库**：SQLite（EF Core），自选股 / 设置 / 每日成交额历史 / 当日行情快照 / 预警规则 / 日K历史 / AI 分析缓存全量入库；首启动自动从旧版 `stock_config.json` / `history_amount.json` 导入
- **数据备份**：设置 / 自选股 / 历史数据 JSON 备份导出导入（合并去重）
- **增强功能**：个股涨跌幅阈值 Windows 通知预警、开机自启动、单实例互斥、窗口越界自动拉回、键盘删除自选、指数 / ETF / 个股自动分组、行内迷你走势图（分时 Sparkline）
- **主题**：深色 / 浅色 / 跟随系统

### AI 行情分析

支持配置 DeepSeek API（Base URL / API Key / 模型 / 超时均可配置，API Key 使用 Windows DPAPI 加密存储），基于本地行情、最近 120 根日K、均线及成交量数据，对当前股票 / ETF / 指数进行趋势结构、量价关系、关键位置及风险观察分析。支持一键获取可用模型列表与连接测试。

AI 分析仅用于行情数据解释，不构成投资建议。

## 当前版本

v0.3.1（AI Analysis）

## 许可证

本项目基于 [GPL-3.0](LICENSE) 发布。

## 系统要求

- Windows 10 1809+ / Windows 11（x64）
- 发布产物为自包含单文件 exe，无需安装 .NET 运行时；源码开发需 .NET 10 SDK

## 项目结构

```
StockWidget/
├── StockWidget.slnx
├── nuget.config              # 华为云 NuGet 镜像（本机代理访问 nuget.org 不稳定）
├── src/
│   ├── StockWidget.Core/     # 领域与服务层（零 UI 依赖）
│   │   ├── Data/             #   SQLite（EF Core）：实体 / DbContext / 迁移 / 路径解析
│   │   ├── Models/           #   设置、行情、AI 配置与分析上下文
│   │   └── Services/         #   行情、自选、日K归档 / 回补、AI 分析（DeepSeek）、备份、热键、交易日历
│   └── StockWidget.App/      # WPF 界面：Views / ViewModels / Services / Themes
└── tests/
    └── StockWidget.Tests/    # xUnit 单元测试（148 项）
```

## 开发环境

- .NET 10 SDK（10.0.401 验证通过）
- 依赖：CommunityToolkit.Mvvm、EF Core 10 (SQLite)、Hardcodet.NotifyIcon.Wpf、Microsoft.Extensions.DependencyInjection、System.Security.Cryptography.ProtectedData（API Key DPAPI 加密）

## Build

```
dotnet build StockWidget.slnx -c Debug
```

## Run

```
dotnet run --project src/StockWidget.App
```

## Test

```
dotnet test StockWidget.slnx
```

## Release

```
dotnet publish src/StockWidget.App -c Release -r win-x64
# 产物：src/StockWidget.App/bin/Release/net10.0-windows/win-x64/publish/StockWidget.App.exe
```

## 文档

- [CHANGELOG.md](CHANGELOG.md)
- [AGENTS.md](AGENTS.md)（仓库规则，AI 协作前必读）
