# Chert Launcher / 燧石启动器 — Minecraft 启动器 (WPF)

> **当前版本：v2.6.0（内测 / Beta）** · 语言：C# / WPF / .NET 10 · 平台：Windows

燧石启动器（Chert Launcher，原 MCLCS）是一个用 C# / WPF 实现的 Minecraft 启动器，覆盖版本安装、启动、崩溃修复、下载、Mod 管理与工具箱等。本项目与 [MCLCS-Linux](https://cnb.cool/RLRS-Studio/MCLCS-Linux) 共享核心（`Chert.Core`），两端功能持续对齐。

## 功能一览

- **启动与安装**：原版 / Fabric / Forge / Quilt / NeoForge 安装；智能 Java 选择（≥21）；启动前存档兼容性检测与降级；启动预热。
- **崩溃处理**：异常识别与报告，可非破坏性自动修复（内存 / Java / 缺失库），支持始终 / 询问 / 拒绝策略。
- **下载中心**：Modrinth 搜索（版本 / 加载器过滤）；BMCLAPI 镜像优先、失败回退官方；下载队列；像素茶艺地图站接入。
- **Mod 管理**：元数据解析、依赖检查、更新检查、卸载。
- **智能推荐**：本地规则 + 热门榜单，首页 Top4，玩法分区过滤，依赖补全标记。
- **账号系统**：离线 / Microsoft / Authlib-Injector，多账号存储与切换。
- **工具箱（20+ 面板）**：日志、版本列表、版本设置、存档管理、截图管理、性能监控、网络诊断、备份管理器、NBT 编辑、数据包冲突检测、皮肤编辑器、音乐播放器、AI 助手、挂机工作流、开发工具等。
- **外观与皮肤**：暗/亮主题、主题色、字体缩放、UI 风格（standard / android / glass / dynamic）独立设置；皮肤预览与编辑。
- **HUD 叠加**：独立窗口实时显示 FPS / 内存 / CPU / GPU / 延迟，跟随游戏窗口。
- **AI 助手**：外部 API 或本地 Ollama 部署，崩溃解读 / 推荐理由 / Mod 翻译 / 语音助手。
- **挂机工作流**：离线 Token 配置帧率 / 渲染距离 / 音量 / 视角 / 模拟按键 / 鼠标连点 / 循环。
- **在线音乐**：多音源（网易云 / QQ 音乐 / 酷狗等）在线播放，移动端与触屏设备可自定义操控布局。
- **局域网 / 广域网联机**：配对码与 P2P 直连，无需公网服务器也能和朋友一起玩。
- **多语言**：中 / 英双语（zh_CN / en_US），运行时即时切换，无需重启。
- **其他**：年度报告、CLI 命令行、资源包格式修复、最小化到托盘、内置自动更新器。

## 更新日志

- **v2.6.0**（当前 / 内测）：安卓布局（UiStyle=android）下的通知改为安卓式通知卡片（图标 + 应用名 + 相对时间戳）；更新源迁移到启动器专属 GitHub Pages（[chert-launcher.github.io](https://chert-launcher.github.io/latest.json)），WPF / Linux / Android 三端共用同一份 `latest.json`；发布包拆分为 GUI 自包含 / 轻量 / CLI 三种形态，按当前安装形态自动选包。
- **v2.5.6**：品牌更名 MCLCS → 燧石启动器 / Chert Launcher；发布双包形态（自包含 + 轻量 Light）；更新源迁移至 GitHub Pages（`latest.json`），下载托管至 CNB Release。
- **v2.5.5**：对齐 MCLCS-Linux 的收官修复批次——工具箱移除已废弃的「文件变更检测」，新增「版本列表」与「版本设置」；添加服务器弹窗复用全局模态样式；崩溃分析页暗色配色修复；存档扫描对缺失 `level.dat` 的目录标记为警告。
- **v2.5.4**：更新源迁移至 CNB Pages 托管的 `latest.json`，国内直连、稳定、免代理；自更新改为「下载 → 解压 → 原地替换安装目录并接力启动」；GitHub 仓库仅作代码镜像。
- **v2.5.3**：启动器自身崩溃捕获与日志（`chert_crash.log`）；崩溃自动修复新增资源包/光影类别；新增存档损坏检测（只读，三色分级）；Mod 冲突禁用在「始终」策略下先弹窗确认。
- **v2.5.2**：修复开发工具，离线自检 SelfCheck 程序 52 项断言全部 PASS。
- **v2.5.1**：接入多分辨率应用图标与托盘图标；修正下载队列按钮置灰反馈。
- **v2.5.0**：升级 Mojang 版本清单至 Piston v2；修复安装器版本选择缺陷；新增最小化到托盘；HUD 叠加层覆盖全部启动路径。
- **v2.4.2**：中英双语运行时即时切换；CLI 升级至 .NET 8 与 GUI 同框架；发布包同时含 GUI 与 CLI。
- **v2.4**：重写收官——四色索引贴主标签、工具箱全局侧边栏、AI 助手、皮肤编辑器（3D 预览）、HUD 叠加、年度报告、挂机工作流。
- **v2.0 – v2.1**：WPF 重写期，引入下载中心、崩溃智能修复、存档降级、多语言与暗亮主题。
- **v0.1 – v1.1**：WPF 起步，下载中心、Modrinth 接入、崩溃分析与智能修复诞生。

## 下载与安装

发布包（v2.6.0）从 [CNB Releases](https://cnb.cool/RLRS-Studio/Chert-WPF/-/releases) 分发，提供四种形态：

| 包 | 文件名 | 说明 | 依赖 |
| --- | --- | --- | --- |
| **GUI 自包含（主用）** | `Chert-Launcher-2.6.0-win-x64-gui.zip` | GUI 启动器，内嵌完整 .NET 10 运行时 | 无 |
| **GUI 轻量（Light）** | `Chert-Light-2.6.0-win-x64.zip` | GUI 启动器，体积小 | 需目标机已装 .NET 10 桌面运行时 |
| **GUI+CLI 合体** | `Chert-Launcher-2.6.0-win-x64.zip` | GUI 与 CLI 同包，兼容旧用户 | 无 |
| **CLI 自包含** | `chert-cli-2.6.0-win-x64.zip` | 仅命令行工具 `chert.exe` | 无 |

解压后进入 `Chert Launcher` 文件夹，运行 `Chert.App.exe`（GUI）或 `chert.exe`（CLI）即可，无需安装。

> **内置自动更新**：启动时静默读取 `latest.json`（[chert-launcher.github.io](https://chert-launcher.github.io/latest.json)），发现新版本后按当前安装形态自动选包、下载 CNB Release 直链、解压并原地替换安装目录、接力启动新版本，全程无需手动下载。

## 编译与发布

- **环境**：Windows + .NET 10 SDK（WPF 仅 Windows 运行）。
- **一键构建四包（含签名位）**：`VER=2.6.0 ./tools/publish-split.sh --build-only`（构建完成后脚本会暂停，待你对 exe 做数字签名后，再执行 `VER=2.6.0 ./tools/publish-split.sh --sign-done` 验签并压缩）。
- **发布 GUI（自包含 EXE）**：
  ```powershell
  dotnet publish src/Chert.App/Chert.App.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true
  ```
- **发布 CLI**：
  ```powershell
  dotnet publish tools/Chert.Cli/Chert.Cli.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true -p:SelfContained=true -p:IncludeNativeLibrariesForSelfExtract=true
  ```
- **签名**：证书在用户手上，构建脚本不自动签名；签名完成后由 `--sign-done` 续做验签 + 压缩。
- **CLI 命令**：`launch` / `list` / `install` / `modpack` / `mods` / `skin` / `version`。

## 仓库结构

| 路径 | 说明 |
| --- | --- |
| `src/` | 当代源码（`Chert.Core` 共享核心 / `Chert.App` WPF 界面） |
| `tools/` | CLI（`Chert.Cli`）与辅助工具 |
| `tests/` | 单元测试 |
| `updates/` | `latest.json` 本地副本（发布时同步到 GitHub Pages） |
| `docs/` | 开发文档与构建说明 |

## 法律声明

本软件以 **Apache License 2.0** 开源发布，详见 `LICENSE`。

- 本软件的分发物**不包含 Minecraft 核心游戏文件**（如版本 jar、assets、libraries）；这些资源由用户在运行游戏时从 **Mojang 官方源及其授权镜像**（如 BMCLAPI）按需下载，用户须持有合法的正版账号。
- 外置登录（Authlib-Injector）仅用于 **littleskin 等皮肤站**及**用户自有 / 授权的私服**，不用于绕过正版验证。
- 本项目与 Mojang / Microsoft 无关，非官方产品。
