# AGENTS.md — 股票小插件 (StockWidget)

## 项目说明

- C# / WPF (.NET 10) 桌面股票行情悬浮窗，从 Python/PySide6 旧版（`D:\AIProjects\stockTool`，最终版 v1.0.3.10）重写。
- 需求基线：旧版全部功能 + 桌面体验包 / 表格增强包 / 分时迷你走势图增强 + 本地 SQLite 数据库存储。
- 数据源：腾讯行情 `qt.gtimg.cn`（GBK 编码）；分时：腾讯分时接口。
- UI：无边框悬浮窗，深色 / 浅色 / 跟随系统三态主题，现代玻璃拟态风格。
- 领域逻辑放 `StockWidget.Core`（零 UI 依赖），界面放 `StockWidget.App`，测试放 `StockWidget.Tests`。

## Repository Rule

- Product Name：股票小插件（StockWidget）
- Repository：stock-widget
- Visibility：Private
- Default Branch：main
- Version：Semantic Versioning
- Tag：vMAJOR.MINOR.PATCH
- Commit：Conventional Commits
- Release：GitHub Releases
- Build Artifact：不得长期提交到 Git History
- Secret：不得提交真实凭据
- Documentation：YYYY-MM-DD-内容-vX.Y.md

修改前必须检查 Git Status、当前 Branch 和 Remote。
不得覆盖用户已有未提交修改。
未经明确授权，禁止执行 git reset --hard、git clean -fd、git push --force 或删除 Repository、Branch、Tag、Release。
正式发布前必须完成 Build、Test、CHANGELOG、版本一致性、Tag、Release 和 Secret 检查。

## 工程约定

- NuGet 源：本仓库 `nuget.config` 固定走华为云镜像（本机代理访问 nuget.org 不稳定），不要删除或改回直连。
- 数据库：SQLite + EF Core，库文件默认 `<exe目录>\data\stockwidget.db`（便携优先，无写权限回退 `%APPDATA%\StockWidget\`）；Schema 变更走 EF Core 迁移，禁止 `EnsureCreated()` 与迁移混用。
- 中文界面文案、中文注释；代码标识符用英文。
- 行情字段映射、`auto_fix_code` 归一化规则、量能计算口径必须与旧版保持一致，改动前先对照旧版源码。
- WPF 线程模型：UI 更新必须在 UI 线程（Dispatcher），网络 IO 全部异步（async/await），禁止 `.Result` / `.Wait()` 死锁写法。
- 全局热键用 Win32 `RegisterHotKey`，不要引入低级键盘钩子。
- 通知用托盘气泡（`ShowBalloonTip`，Win10+ 自动渲染为 Toast），不引入 Windows Toolkit 通知包。

## 常用命令

```
dotnet build StockWidget.slnx -c Debug
dotnet run --project src/StockWidget.App
dotnet test StockWidget.slnx
dotnet publish src/StockWidget.App -c Release -r win-x64
```
