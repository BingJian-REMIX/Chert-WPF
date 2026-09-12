# Chert Launcher / 燧石启动器 — Minecraft 启动器 (WPF)

> **当前版本：v2.6.0（公测正式版）** · C# / WPF / .NET 10 · Windows
> **路线图**：`v2.6.0` 为进入**公测（公开测试）**的正式版本；后续转入稳定迭代。

燧石启动器（Chert Launcher，原 MCLCS）是一个用 C# / WPF 实现的 Minecraft 启动器，覆盖版本安装、启动、崩溃修复、下载、Mod 管理与工具箱等。本项目与 [MCLCS-Linux](https://cnb.cool/RLRS-Studio/MCLCS-Linux) 共享核心（`Chert.Core`），两端功能持续对齐。

- 主仓库（GitHub）：<https://github.com/BingJian-REMIX/Chert-Launcher>
- 更新信息源（GitHub Pages）：<https://remix-laser-raising-studio.github.io/Chert-upgrade/latest.json>
- 下载包（CNB Release）：<https://cnb.cool/RLRS-Studio/Chert-Launcher/-/releases>

## 产物命名

| 组件 | 文件名 | 说明 |
| --- | --- | --- |
| GUI 启动器 | **`Chert Launcher.exe`** | 主程序（带空格的程序名） |
| CLI 命令行 | **`chert.exe`** | 同目录发布，命令行工具（`launch` / `list` / `install` / `modpack` / `mods` / `skin` / `version`） |

## 发布包两种形态

| 形态 | 资产名 | 体积 | 依赖 | 适用 |
| --- | --- | --- | --- | --- |
| **自包含（推荐）** | `Chert-Launcher-2.6.0-win-x64.zip` | ~120 MB | 无，内嵌完整 .NET 10 运行时 | 双击即跑、零依赖 |
| **轻量（Light）** | `Chert-Light-2.6.0-win-x64.zip` | ~1 MB | 需目标机已装 **.NET 10 桌面运行时** | 体积敏感、已装运行时的用户 |

> 轻量版（framework-dependent）将运行时甩给系统，思路与 HMCL 的 9MB 单 jar 一致；自包含版与之相反，把整套运行时打进 exe，换取「免安装」。
> 自动更新器会**按当前安装形态自动选包**：当前是 light 版则更新到 light 包，是自包含版则更新到自包含包。

## 功能一览

- **启动与安装**：原版 / Fabric / Forge / Quilt / NeoForge 安装；智能 Java 选择（≥21）；启动前存档兼容性检测与降级；启动预热。
- **崩溃处理**：异常识别与报告，可非破坏性自动修复（内存 / Java / 缺失库），支持始终 / 询问 / 拒绝策略。
- **下载中心**：Modrinth 搜索（版本 / 加载器过滤）；BMCLAPI 镜像优先、失败回退官方；下载队列；像素茶艺地图站接入。
- **Mod 管理**：元数据解析、依赖检查、更新检查、卸载。
- **智能推荐**：本地规则 + 热门榜单，首页 Top4，玩法分区过滤，依赖补全标记。
- **账号系统**：离线 / Microsoft / Authlib-Injector，多账号存储与切换。
- **工具箱（20+ 面板）**：日志管理、版本列表、版本设置、存档管理、截图管理、性能监控、网络诊断、备份管理器、NBT 编辑、数据包冲突检测、皮肤编辑器、音乐播放器、AI 助手、挂机工作流、开发工具等。
- **外观与皮肤**：暗/亮主题、主题色、字体缩放、独立设置；皮肤预览与编辑。
- **HUD 叠加**：独立窗口实时显示 FPS / 内存 / CPU / GPU / 延迟，跟随游戏窗口。
- **AI 助手**：外部 API 或本地 Ollama 部署，崩溃解读 / 推荐理由 / Mod 翻译 / 语音助手。
- **挂机工作流**：离线宏工作流——`F` 功能键 / `D` 延时 / `L` 长按 / `K` 键码 / `C` 鼠标左键连点 / `R` 鼠标右键连点 / `M` 鼠标移动 / `S` 滚轮 / `T` 文本 / `G` 按住 / `U` 松开 / `J` 随机等待 / `E` 按名称按键(全键盘) / `*` 循环；WPF 与 Linux 双端统一解析与执行。
- **多语言**：中 / 英双语（zh_CN / en_US），运行时即时切换，无需重启。
- **其他**：年度报告、CLI 命令行、资源包格式修复、最小化到托盘、自动更新器。

## 更新日志

- **v2.6.0**（公测正式版）：
  - **正式定名 Chert Launcher / 燧石启动器**：全量重命名（命名空间 `Chert.*`、产物 `Chert Launcher.exe` / `chert.exe`、更新源 `Chert-upgrade`）。
  - **发布双包**：新增轻量版 `Chert-Light`（framework-dependent 单文件，约 1MB，需 .NET 10 桌面运行时）；自包含版 `Chert-Launcher` 保持双击即跑零依赖。
  - **自动更新按安装形态选包**：当前为 light 版则更新到 light 包，自包含版则更新到自包含包（latest.json 新增 `lightAvailable` / `lightDownloadUrl` 字段）。
  - 自动更新兜底直链资产名由 `MCLCS-*` 收敛为 `Chert-Launcher-*` / `Chert-Light-*`。
- **v2.5.6**（公测前最终功能版，开发中）：
  - **挂机工作流全面重构**：统一 WPF / Linux 双端宏格式（Core 解析器字节一致），词汇表由旧版「帧率/渲染距离/音量/视角」升级为完整键鼠宏——`F` 功能键 / `D` 延时 / `L` 长按 / `K` 数字键码 / `C` 左键连点 / `R` 右键连点 / `M` 鼠标移动 / `S` 滚轮 / `T` 文本 / `G` 按住 / `U` 松开 / `J` 随机等待 / `E` 按名称按键(全键盘) / `*` 循环；新增 `E` 全键盘动作、`G/U` 同时支持键码与名称；WPF 与 Linux 各新建 `AfkRunner` 执行器（SendInput / xdotool），编辑器支持类型选择弹窗、`T` 明文编辑与运行/停止。
  - **工具箱侧边栏「自动居中」滚动**：副页过多可滚动时，点击 / 键盘上下键 / 首次加载 / 窗口尺寸变化都会把当前选中项平滑滚到侧边栏垂直中央，列表不足一屏或触顶/触底时自然停靠边界。
  - **3D 皮肤预览锐化**：修复长期「糊」问题——每面按 8× 最近邻放大后补至 2 的幂画布，避开 WPF 3D 双线性放大与被 mipmap 缩小两条糊化路径；同时修正 NPOT 四肢缺面与冻结位图回归。
  - 多项暗色主题画笔修复、崩溃分析页与性能页稳定化、`IsFrozen` 写像素回归修复等。
- **v2.5.5**（上一个发布版）：对齐 MCLCS-Linux 的收官修复批次——工具箱全局侧边栏移除已废弃的「文件变更检测」，新增「版本列表」与「版本设置」入口；添加服务器弹窗复用全局模态样式（暗色下不再呈黑块）；崩溃分析页补充主题画笔修复暗色配色；存档扫描对缺失 `level.dat` 的目录标记为警告而非误报兼容；GUI 产物定名为 `Chert Launcher.exe`、CLI 为 `chert.exe`。
- **v2.5.4**：更新源迁移至 GitHub Pages 托管的 `latest.json`，稳定、免代理；自更新改为「下载 → 解压 → 原地替换安装目录并接力启动新版本」；发布物改为单个 `MCLCS-2.5.4-win-x64.zip`；GitHub 仓库为主仓库。
- **v2.5.3**：启动器自身崩溃捕获与日志（`chert_crash.log`）；崩溃自动修复新增资源包/光影类别；新增存档损坏检测（只读，三色分级）；Mod 冲突禁用在「始终」策略下先弹窗确认。
- **v2.5.2**：修复开发工具，离线自检 SelfCheck 程序 52 项断言全部 PASS。
- **v2.5.1**：接入多分辨率应用图标与托盘图标；修正下载队列按钮置灰反馈。
- **v2.5.0**：升级 Mojang 版本清单至 Piston v2；修复安装器版本选择缺陷；新增最小化到托盘；HUD 覆盖全部启动路径。
- **v2.4.2**：中英双语运行时即时切换；CLI 升级至 .NET 8 与 GUI 同框架；发布包同时含 GUI 与 CLI。
- **v2.4**：重写收官——四色索引贴主标签、工具箱全局侧边栏、AI 助手、皮肤编辑器（3D 预览）、HUD 叠加、年度报告、挂机工作流。
- **v2.0 – v2.1**：WPF 重写期，引入下载中心、崩溃智能修复、存档降级、多语言与暗亮主题。
- **v0.1 – v1.1**：WPF 起步，下载中心、Modrinth 接入、崩溃分析与智能修复诞生。

> 更完整的历史快照见 `history` 分支。

## 下载与安装

发布包有两种形态（见上「发布包两种形态」）：**自包含版**内嵌完整 .NET 10 运行时，解压即用、零依赖；**轻量版**需目标机已安装 .NET 10 桌面运行时，但体积小约 1MB。

| 版本 | 资产 | 说明 |
| --- | --- | --- |
| **v2.6.0（最新）** | [CNB Releases](https://cnb.cool/RLRS-Studio/Chert-Launcher/-/releases) | 自包含免运行时 / 轻量需 .NET 10 桌面运行时 |
| 全部历史版本 | [CNB Releases](https://cnb.cool/RLRS-Studio/Chert-Launcher/-/releases) | 各版本发布直链 |

下载后直接解压，运行 `Chert Launcher.exe` 即可。启动器内置**自动更新器**：启动时读取 GitHub Pages 上的 `latest.json`，发现新版本后直接下载 CNB Release 直链、解压并原地替换安装目录、接力启动新版本，全程无需手动下载或 winget。自动更新会按当前安装形态（自包含 / 轻量）自动选择对应的包。

## 编译与发布

- **环境**：Windows + .NET 10 SDK（WPF 仅 Windows 运行）。
- **发布 GUI（自包含 single-file）**：
  ```powershell
  dotnet publish src/Chert.App/Chert.App.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true
  # 产物 Chert.App.exe 重命名为「Chert Launcher.exe」
  ```
- **发布 CLI（自包含 single-file，同目录供 GUI 调用）**：
  ```powershell
  dotnet publish tools/Chert.Cli/Chert.Cli.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true -p:SelfContained=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true
  # 产物 chert.exe 复制到 GUI 发布目录（与 Chert Launcher.exe 同目录）
  ```
- **发布轻量版（framework-dependent，CLI 须多文件，不可单文件）**：
  ```powershell
  dotnet publish src/Chert.App/Chert.App.csproj -c Release -r win-x64 `
    -p:PublishSingleFile=true -p:SelfContained=false -p:EnableWindowsTargeting=true
  dotnet publish tools/Chert.Cli/Chert.Cli.csproj -c Release -r win-x64 `
    -p:SelfContained=false -p:EnableWindowsTargeting=true
  # GUI 的 Chert.App.exe 重命名为「Chert Launcher.exe」；CLI 多文件（chert.exe + chert.dll + Chert.App.dll + Chert.Core.dll）与 GUI 同目录
  ```
- **合并打包**：自包含版将 `Chert Launcher.exe` 与 `chert.exe`（及各自的 `.dll` / `.pdb` 已内联为 single-file）放入同一目录，压缩为 `Chert-Launcher-2.6.0-win-x64.zip`；轻量版将 GUI 单文件与 CLI 多文件同目录压缩为 `Chert-Light-2.6.0-win-x64.zip`，均作为 CNB Release 资产。
- **Linux 交叉编译校验**：可用 Roslyn 直接引用 .NET 10 参考程序集完成 App / CLI 层编译校验（详见 `docs/BUILD.md`）。
- **CLI 命令**：`launch` / `list` / `install` / `modpack` / `mods` / `skin` / `version`。

## 仓库结构

| 路径 | 说明 |
| --- | --- |
| `src/` | 当代源码（Core / App） |
| `tools/` | CLI（`Chert.Cli`）与辅助工具 |
| `tests/` | 单元测试 |
| `history` 分支 | 历史演进快照（原型与旧版本源码） |
| `docs/` | 开发文档与构建说明 |
| `dist/`（仅 `history` 分支） | 各版本发布包（ZIP），不随源码主线入库 |

> 发布包（dist/）归档于 `history` 分支；源码主线（main）只包含当代源码与文档，不含构建产物。

## 法律声明

本软件以 **MIT 许可证** 开源发布，详见 `LICENSE`。

- 本软件的分发物**不包含 Minecraft 核心游戏文件**（如版本 jar、assets、libraries）；这些资源由用户在运行游戏时从 **Mojang 官方源及其授权镜像**（如 BMCLAPI）按需下载，用户须持有合法的正版账号。
- 外置登录（Authlib-Injector）仅用于 **littleskin 等皮肤站**及**用户自有 / 授权的私服**，不用于绕过正版验证。
- 本项目与 Mojang / Microsoft 无关，非官方产品。
