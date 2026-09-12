# MCLCS 待开发清单（核查报告）

> 核查时间：2026-08-07　核查方式：逐文件阅读 `src/Chert.Core` + `src/Chert.App` + `tools/`，对照《MCLCS 启动器完整需求规格说明书》
> 判定标准：不看文件是否存在，只看**功能是否真的可用**（空壳/占位/未绑定 Command 一律判为未完成）

## 总览

| 层次 | 状态 |
|---|---|
| **Core 逻辑层** | 覆盖度高，约 85% 规格已有对应实现 |
| **App UI 层** | 明显落后于 Core，大量已完成的 Core 能力没有界面入口 |
| **主要矛盾** | **不是缺算法，是缺接线** —— Core 写好了，UI 没接 |

一个典型例证：`LauncherProfile` 里 `TabTheme` / `Hud` / `Prewarm` / `Backup` / `ServerPackCacheMb` / `AutoRepairResourcePacks` / `AfkWorkflows` / `ShaderTokens` 字段**全部已定义**，而 `SettingsViewModel` 对这些字段的引用数为 **0**。数据模型已铺好路，纯缺绑定。

---

## 一、完全未开发（Core 与 UI 均无）

| # | 功能 | 规格出处 | 说明 |
|---|---|---|---|
| 1 | **Mod 开发环境** | 2.3-11a | 选加载器/版本，一键生成项目骨架。全仓库无任何骨架生成代码 |
| 2 | **资源包/数据包创建器** | 2.3-11b | 模板卡片入口、引导创建、自动生成 JSON。无模板、无生成器 |
| 3 | **命令语法表** | 2.3-11d | 仅 `SidebarModel.cs:174` 有一条注册项，无命令数据集、无分类/搜索/收藏/AI 解释 |
| 4 | **皮肤编辑器（像素引擎）** | 2.3-13 | `SkinEditor.cs` 只有 36 区域坐标表 + PNG 尺寸校验，注释明说"像素绘制由界面层完成"。缺：36 面编辑画布、网格辅助、对称绘制、64×64 展开图实时预览、导出 PNG、应用到离线账号。`SkinView.xaml` 标题是"皮肤**预览**"，只做 Mojang 查询 + 3D 旋转 |
| 5 | **Toast 通知基础设施** | 2.3-16 | 右下角非阻塞 5 秒通知。全工程零 Toast/Notification/NotifyIcon |
| 6 | **成就展示** | 2.3-2 | 存档管理器内嵌成就。全仓库无 `advancements`/`stats` JSON 解析 |
| 7 | **启动前兼容性汇总弹窗** | 2.3-16 | "启动前检测到以下兼容性问题" + 一键修复/忽略并启动/取消/稍后处理四按钮。`LaunchCoordinator` 无此逻辑 |
| 8 | **GC 调优建议** | 2.3-5 | 性能监控要求给出 GC 建议，无任何相关逻辑 |
| 9 | **语音助手** | 2.3-15 / 3.11 | 按住说话 + 系统原生识别。全工程零 Voice/Speech |
| 10 | **配装推荐** | 3.11 | `Ai/Assistant.cs` 只有崩溃解读/推荐理由/Mod 翻译三项 |

---

## 二、Core 已就绪，只缺 UI（性价比最高，优先做）

这批 Core 代码质量不错、可直接复用，补一层 View/ViewModel 即可交付。

| # | 功能 | Core 文件 | 缺什么 |
|---|---|---|---|
| 11 | **备份管理器** | `Toolbox/BackupManager.cs`（Create/Restore/Delete/Prune/SelectExpired 完备） | 无 View/VM、未挂载。另缺定时调度（`BackupPolicy` 只有 `AutoBeforeLaunch`）、绝对路径支持、恢复前自动备份 |
| 12 | **NBT 编辑器** | `Toolbox/NbtEditor.cs`（路径寻址/类型校验/增删改名/RenderTree 完备） | 无 View/VM、未挂载。且**没有任何 Save 方法**，规格要求的"保存自动备份原文件"不存在 |
| 13 | **数据包冲突检测** | `Toolbox/DataPackConflictDetector.cs` | 无 View/VM、未挂载。另缺命名空间 ID 级冲突（现只比完整路径字符串）、内置规则库联网更新、点击跳转定位 |
| 14 | **文件变更检测** | `Toolbox/FileChangeDetector.cs`（快照/SHA-256/增删改比对完备） | App 层**零引用**。无 View/VM、无启动或焦点回归触发、无 Toast |
| 15 | **HUD 叠加窗口** | `Hud/HudConfig.cs` + `HudMetricsProvider.cs` | 无独立窗口、无点击穿透、无跟随游戏窗口、无拖拽。设置页也无开关 |
| 16 | **挂机工作流配置** | `Tokens/AfkWorkflowToken.cs` | App 层零引用。无动作序列编辑器、无 Token 复制/导入 UI |
| 17 | **光影配置 Token** | `Tokens/ShaderConfigToken.cs` | App 层零引用，无任何 UI 入口 |
| 18 | **年度报告** | `Statistics/AnnualReport.cs` | App 层零引用。游戏页统计区缺入口卡片，无报告页面、无导出 Token 分享、无 AI 解读接入 |
| 19 | **服务器资源包缓存** | `Resources/ServerResourcePackCache.cs` | 无可视化列表（服务器 IP/文件名/大小/时间），无清理/导出，设置页无开关 |
| 20 | **启动预热** | `Launcher/LaunchPrewarmer.cs` | App 层**零引用**，启动器启动时根本没调用。设置页无开关 |

---

## 三、UI 存在但功能不达标

| # | 功能 | 现状 | 缺口 |
|---|---|---|---|
| 21 | 日志管理器 | 可用 | 缺**实时跟踪**（无 FileSystemWatcher/tail）、关键字命中高亮（现只按级别整行着色）、级别多选 |
| 22 | 存档管理器 | 只做兼容性扫描/降级/回滚 | 缺**备份/恢复/删除/复制**四大操作、存档大小与游玩时长展示、成就展示 |
| 23 | 种子库 | **已移出（2026-08-12）** | 经商议决定移除：在线种子库为死代码（App 层零调用方），`src/Chert.Core/Toolbox/SeedLibrary.cs` 已删除、`Chert.SelfCheck` 的 `SeedLibraryTest` 已移除。本地存档种子提取（`SavesViewModel.ExtractSeedCommand`）保留，与种子库无关 |
| 24 | 截图管理器 | 纯文字 ListBox | 缺缩略图网格、查看大图 |
| 25 | 性能监控 | 只显示实例数 + 核心数 + 累计分钟 | `PerfViewModel.cs:47` **完全没用** `HudMetricsProvider`。缺实时 FPS/内存/CPU **曲线图** |
| 26 | 快捷方式生成器 | 可生成 | `ShortcutGenerator.cs:34` 支持 iconPath，但 UI 无图标选择控件、调用未传参 → **自定义图标不可用** |
| 27 | AI 助手 | 两栏粘贴文本框 + 按钮 | 规格要求"左侧 6 项功能列表 + 右侧对话气泡"。现无功能列表、无气泡、无多轮上下文、无语音图标 |
| 28 | 游戏页-局域网 | 扫描/刷新/空提示 OK | `JoinLanAsync` 仅启动默认版本，**未真实连接**（注释"留待 v2.2"）。无密码锁提示 |
| 29 | 游戏页-服务器列表 | 列表 + 延迟灯 OK | **添加按钮无 Command 绑定**、加入仅启动不连接、无右键编辑/删除 |
| 30 | 游戏页-智能推荐 | 卡片 + 红标 OK | **"一键安装"和"不感兴趣"两个按钮均无 Command 绑定**，点了没反应 |
| 31 | 游戏页-统计区 | 最近版本 OK | 本周时长/崩溃次数**硬编码 "—"**；年度报告入口缺失 |

---

## 四、规格符合度问题（已实现但不符合规范）

| # | 问题 | 规格原文 | 现状 |
|---|---|---|---|
| 32 | **开关控件违规** | 1.4「所有开关使用 Toggle Switch（灰色/主题色滑动），**无复选框**」 | `Controls.xaml:15` 已定义 `ToggleSwitchStyle`，但**全工程零引用**。`SettingsView.xaml` 有 10 个 `<CheckBox>`，另有 ModpackView 5 个、RedundantCleanView 2 个等 |
| 33 | **侧边栏点击不保持展开** | 1.4「点击切换内容区，**点击后保持展开**」 | `SidebarModel.cs` 悬停展开已实现，但 `SidebarState` 注释"已按模板移除"，点击后仍会自动收起 |
| 34 | **工具箱不是侧边栏** | 2.3「左侧副标签，入口图标+文字」 | `ToolboxView.xaml` 用的是 `TabControl`，且只挂 **11 个** TabItem，而 `ToolboxCatalog.RequiredPanelCount = 16` |
| 35 | **工具箱侧栏路由死链** | — | `MainWindow.xaml.cs:269` 的 `SelectSidebarItem` **只处理 `MainTabKind.Download`**，`Sidebar.Toolbox` 的 21 项点击全部无跳转 |

---

## 五、设置页缺失项

`SettingsViewModel.cs` 对以下字段引用数均为 **0**（而 `LauncherProfile` 中多数已定义）：

| 分区 | 缺失开关 | Profile 字段 |
|---|---|---|
| 通用 | 文件变更检测开关 | ❌ 需新增 |
| 启动 | 启动预热开关 | ✅ `Prewarm` 已有 |
| 启动 | HUD 叠加开关 | ✅ `Hud` 已有 |
| 启动 | 启动前兼容性检测开关 | ❌ 需新增 |
| 下载 | 资源包自动修复开关 | ✅ `AutoRepairResourcePacks` 已有 |
| 下载 | 服务器资源包缓存开关 | ✅ `ServerPackCacheMb` 已有 |
| 外观 | 索引贴颜色（四色独立设置） | ✅ `TabTheme` 已有 |

---

## 六、已确认完成（无需处理）

网络诊断、冗余清理、整合包导入导出、音乐播放器（含状态栏迷你条/三音源/启动联动）、顶部标题栏、四色索引贴主标签（重叠+下划线+200ms 动画）、页面切换动画、底部状态栏、卡片流样式、模态弹窗样式、下载页全部（副标签/搜索保留输入/队列暂停取消/地图页像素茶艺 API 全套）、CLI 七命令（launch/list/install/modpack/mods/skin/version）、多语言 `LocaleManager` 接入、启动器自更新、正版皮肤查询。

---

## 建议实施顺序

**第一梯队（Core 就绪，纯补 UI，见效最快）**
→ 11 备份管理器、12 NBT 编辑器、13 数据包冲突、14 文件变更检测 + 5 Toast、18 年度报告、19 服务器资源包缓存

**第二梯队（规格符合度，影响全局观感）**
→ 32 全局换 ToggleSwitch、34+35 工具箱改侧边栏并补齐 16 面板路由、33 侧边栏点击保持展开、五 设置页补 7 个开关

**第三梯队（补齐半成品）**
→ 30 推荐按钮绑定、29 服务器添加/右键、31 统计真实数据、25 性能曲线、22 存档四操作、27 AI 助手改版

**第四梯队（大工程新开发）**
→ 4 皮肤编辑器像素引擎、1+2+3 开发工具三件套、15 HUD 叠加窗口、16 挂机工作流编辑器、28/29 真实连接服务器
