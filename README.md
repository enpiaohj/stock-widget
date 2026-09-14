# 股票小插件 (StockWidget)

Windows 桌面股票行情悬浮窗小工具。C# / WPF 全新重写版， predecessor 为 Python/PySide6 单文件版本（`D:\AIProjects\stockTool`）。

## 产品简介

常驻桌面的半透明行情悬浮窗：实时显示自选股（指数 / ETF / 个股）行情，统计沪深两市总成交额并与上一交易日对比增量 / 缩量，支持全局热键一键显隐、窗口锁定、托盘常驻，数据存储于本地 SQLite 数据库。

## 核心功能

- **实时行情**：腾讯行情接口（`qt.gtimg.cn`），5 秒级自动刷新（1–120 秒可配），12 个字段（名称 / 现价 / 涨跌幅 / 成交量 / 成交额 / 换手率 / 最高 / 最低 / 开盘 / 昨收 / 市值 / 振幅）自由取舍
- **量能统计**：沪深两市总成交额（亿），较上一交易日增量（红）/ 缩量（绿），历史入库可回看
- **悬浮窗体验**：无边框圆角、置顶 / 取消置顶、透明度调节、位置锁定、表头拖拽移动、双击表头显隐、内容自适应尺寸
- **全局热键**：`RegisterHotKey` 实现，任意组合可配，默认 `Ctrl+Q`
- **系统托盘**：双击显隐、窗口置顶 / 取消、初始化位置、退出；图标叠加大盘涨跌色点
- **本地数据库**：SQLite（EF Core），自选股 / 设置 / 每日成交额历史 / 当日行情快照 / 预警规则全量入库；首启动自动从旧版 `stock_config.json` / `history_amount.json` 导入
- **增强功能**：个股涨跌幅阈值 Windows 通知预警、开机自启动、单实例互斥、窗口越界自动拉回、列头排序、指数 / ETF / 个股自动分组、行内迷你走势图（分时 Sparkline）
- **主题**：深色 / 浅色 / 跟随系统

## 当前版本

v1.0.0（开发中）

## 系统要求

- Windows 10 1809+ / Windows 11（x64）
- 发布产物为自包含单文件 exe，无需安装 .NET 运行时；源码开发需 .NET 10 SDK

## 项目结构

```
StockWidget/
├── StockWidget.slnx
├── nuget.config              # 华为云 NuGet 镜像（本机代理访问 nuget.org 不稳定）
├── src/
│   ├── StockWidget.Core/     # 领域与服务层（零 UI 依赖）：行情、数据库、热键、通知、迁移
│   └── StockWidget.App/      # WPF 界面：Views / ViewModels / Controls / Themes
└── tests/
    └── StockWidget.Tests/    # xUnit 单元测试
```

## 开发环境

- .NET 10 SDK（10.0.401 验证通过）
- 依赖：CommunityToolkit.Mvvm、EF Core 10 (SQLite)、Hardcodet.NotifyIcon.Wpf、Microsoft.Extensions.DependencyInjection

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
