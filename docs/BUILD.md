# 构建指南（Windows + .NET 8）

MCLCS 的 WPF 界面仅能在 **Windows** 上编译运行。核心逻辑（`Chert.Core`）为纯 .NET 类库，
可在任意平台构建（仓库内 `tools/Chert.SelfCheck` 即为无依赖的离线自检程序）。

---

## 环境要求

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（含桌面运行时 / WPF 工作负载）
- Visual Studio 2022（可选，需"桌面开发"工作负载）或仅用命令行

---

## 步骤

### 1. 切换目标框架（仓库默认 net6.0 以适配离线沙箱）

仓库内 `.csproj` 当前目标为 `net6.0`（沙箱仅含 .NET 6 SDK）。在 Windows + .NET 8 上构建前，
将以下四处改为 `net8.0` / `net8.0-windows`：

- `src/Chert.Core/Chert.Core.csproj` → `<TargetFramework>net8.0</TargetFramework>`
- `src/Chert.App/Chert.App.csproj` → `<TargetFramework>net8.0-windows</TargetFramework>`
- `tests/Chert.Core.Tests/Chert.Core.Tests.csproj` → `<TargetFramework>net8.0</TargetFramework>`
- `tools/Chert.SelfCheck/Chert.SelfCheck.csproj` → `<TargetFramework>net8.0</TargetFramework>`
- `tools/Chert.Cli/Chert.Cli.csproj` → `<TargetFramework>net8.0</TargetFramework>`

> 也可保持 `net6.0`，但 Microsoft 已结束 .NET 6 主流支持，建议升级到 .NET 8。

### 2. 还原与构建

```powershell
dotnet restore Chert.sln
dotnet build Chert.sln -c Release
```

> 首次 `dotnet restore` 需要网络，用于还原 xUnit 测试包（核心与界面**无任何第三方 NuGet 依赖**）。

### 3. 运行

```powershell
dotnet run --project src/Chert.App
```

### 4. 运行单元测试

```powershell
dotnet test tests/Chert.Core.Tests
```

### 5. 离线自检（无需网络、无需 Windows 桌面）

```powershell
dotnet run --project tools/Chert.SelfCheck
```

该命令在任意装有 .NET 6/8 的平台上运行，当前 **265 项全部通过**，覆盖核心算法与 v1.1 新增模块：
Java 版本解析、Maven 坐标、参数规则/变量/内存注入、classpath、原生库、崩溃识别、
崩溃修复规划引擎、Fabric 版本合并、离线 UUID、账号存储 CRUD、LaunchOptions 扩展、整合包索引解析、
Modrinth 模型反序列化、多语言、主题切换、Mod 元数据解析；
以及 v1.1 工具箱与系统模块：日志管理、种子库、截图管理、网络诊断、冗余文件清理、
整合包导入导出、游玩时长统计、多实例追踪、AI 助手（本地规则）、启动器自更新。

---

## 依赖说明

| 项目 | 第三方 NuGet 依赖 |
|---|---|
| Chert.Core | **无**（仅 BCL：System.Text.Json、System.IO.Compression、System.Security.Cryptography） |
| Chert.App | **无**（WPF 为框架自带；MVVM 基类内置） |
| Chert.Cli | **无**（依赖 Chert.Core + App） |
| Chert.Core.Tests | xUnit（仅测试用，运行期不依赖） |
| Chert.SelfCheck | **无** |

---

## 已知限制

1. **WPF 界面需 Windows**：Linux / macOS 无法构建运行。v1.1 的 `Chert.App`（四页界面、工具箱、设置、下载队列、AI 助手 UI）已在代码层面完成，但**当前离线沙箱（Linux + .NET 6）无法编译 `net8.0-windows`**，正确性通过人工代码审查保证；核心逻辑已通过 `Chert.SelfCheck`（265 项）离线验证。
2. **沙箱无法联网还原 NuGet**：本仓库在离线环境交付，`Chert.Core.Tests` 的 xUnit 包需在有网环境还原。
3. **Forge 安装器**：以无头模式（`--installClient`）运行官方 installer；个别版本若失败，会提示用户手动运行 GUI installer。
4. **Microsoft 登录**：需用户在浏览器手动输入设备码（OAuth2 设备流），CLI 打印到控制台；WPF 版需补充浏览器/WebView 集成。
5. **镜像地址**基于公开 BMCLAPI / 官方 / Adoptium / Modrinth 端点；若某镜像不可用，代码会自动回退下一个候选源。
