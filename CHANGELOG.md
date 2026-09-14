# Changelog

所有对外可感知的变更记录于此。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

### Added（计划中 / 开发中）

- C# / WPF (.NET 10) 全新重写，功能对齐 Python 版 v1.0.3.10 并增强
- 本地 SQLite 数据库（EF Core 10）：设置、自选股、每日成交额历史、当日行情快照、预警规则
- 旧版 `stock_config.json` / `history_amount.json` 自动导入
- 桌面体验包：涨跌幅阈值通知预警、开机自启、单实例互斥、窗口越界拉回
- 表格增强包：列头排序、指数/ETF/个股自动分组、键盘导航
- 分时迷你走势图（行内 Sparkline + 分时弹窗）
- 现代深色玻璃拟态 UI：无边框圆角、系统级背景模糊、数据更新动效、骨架加载态
