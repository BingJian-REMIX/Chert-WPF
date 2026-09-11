using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using MCLCS.Core.Auth;
using MCLCS.Core.Download;
using MCLCS.Core.Installers;
using MCLCS.Core.Launcher;
using MCLCS.Core.Models;
using MCLCS.Core.Profiles;
using MCLCS.Core.Save;
using MCLCS.Core.Utils;

namespace MCLCS.App.Services;

/// <summary>
/// 应用级服务：持有 HttpClient / 下载器 / 游戏目录，并暴露安装、启动、下载 Mod、版本列表等操作。
/// 实现 ILogger 以将进度回传到 UI。
/// </summary>
public class LauncherService : ILogger
{
    public static LauncherService Instance { get; private set; } = new(GameConstants.DefaultGameRoot);

    /// <summary>游戏目录切换后触发，供各页面刷新版本列表 / 存档列表等（bug #26）。</summary>
    public static event Action? GameRootChanged;

    /// <summary>成功安装/下载一个新版本后触发（参数为版本 Id），供游戏页快速启动下拉实时刷新（bug：下载后不显示新版本）。</summary>
    public static event Action<string>? VersionInstalled;

    /// <summary>
    /// 已安装版本集合发生变化（新增 <b>或删除</b>）后触发，供各页面刷新版本列表。
    /// 与 <see cref="VersionInstalled"/> 的区别：后者只在安装成功时带版本 Id 触发，
    /// 删除版本没有「新 Id」可带，因此统一由本事件通知。
    /// </summary>
    public static event Action? VersionListChanged;

    /// <summary>主动通知「已安装版本集合已变化」（删除版本等场景调用）。订阅者异常不影响调用方。</summary>
    public static void RaiseVersionListChanged()
    {
        try { VersionListChanged?.Invoke(); } catch { /* 订阅者异常不得冒泡 */ }
    }

    /// <summary>
    /// 以新的游戏目录重建单例（bug #26：设置 → 启动 中切换 Minecraft 游戏路径）。
    /// 目录未变化时不做任何事，避免无谓地丢弃已建立的 HttpClient 连接池。
    /// </summary>
    public static void Reinitialize(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return;
        var full = Path.GetFullPath(gameRoot);
        if (string.Equals(full, Instance.GameRoot, StringComparison.OrdinalIgnoreCase)) return;

        try { Directory.CreateDirectory(full); } catch { /* 目录不可创建时交由后续操作报错 */ }

        Instance = new LauncherService(full);
        GameRootChanged?.Invoke();
    }

    private readonly HttpClient _client = new();
    private readonly IDownloader _downloader;

    /// <summary>整合包在线源注册表（当前仅 Modrinth 常驻可用）。</summary>
    private readonly IModpackSource[] _modpackSources;

    public string GameRoot { get; }

    public event Action<string>? Logged;

    /// <summary>底层 HttpClient（供外联图标/封面等 UI 层复用，已带 MCLCS User-Agent）。</summary>
    public HttpClient ApiClient => _client;

    /// <summary>用内置下载器下载单个文件到本地（更新包等），支持进度回调与取消。</summary>
    public async Task DownloadFileAsync(string url, string destination, IProgress<double>? progress = null, CancellationToken ct = default)
        => await _downloader.DownloadAsync(new DownloadItem(new[] { url }, destination, null, null), progress, ct);

    /// <summary>像素茶艺（PixelMap）地图站客户端（下载页 → 地图）。</summary>
    public PixelmapClient Pixelmap { get; }

    public LauncherService(string gameRoot)
    {
        GameRoot = gameRoot;
        _downloader = new HttpDownloader(_client, 8, this);

        // 规格 2.2：地图站要求 User-Agent 为 Chert/版本号 (Windows; +仓库地址)
        _client.DefaultRequestHeaders.UserAgent.TryParseAdd(
            $"Chert/{GameConstants.LauncherVersion} (Windows; +{GameConstants.GitHubRepoUrl})");

        Pixelmap = new PixelmapClient(_client);

        // 整合包在线源：Modrinth 免 Key 常驻可用。
        _modpackSources = new IModpackSource[]
        {
            new ModrinthModpackSource(_client)
        };
    }

    /// <summary>当前可用的整合包在线源。</summary>
    public IReadOnlyList<IModpackSource> ModpackSources => _modpackSources;

    /// <summary>按 Id 取得整合包源（未知 Id 回退到 Modrinth）。</summary>
    public IModpackSource GetModpackSource(string? id) =>
        _modpackSources.FirstOrDefault(s => s.Id == (id ?? "")) ?? _modpackSources[0];

    /// <summary>搜索整合包（当前仅 Modrinth，依 <paramref name="sourceId"/> 选择来源）。</summary>
    public Task<List<ModpackItem>> SearchModpacksAsync(string? keyword, string? gameVersion,
        string? loader, string? sourceId, CancellationToken ct)
        => SearchModpacksAsync(keyword, gameVersion, loader, sourceId, ct, 24, 0);

    /// <summary>带分页的整合包搜索（bug #16：整合包页此前无法翻页）。</summary>
    public async Task<List<ModpackItem>> SearchModpacksAsync(string? keyword, string? gameVersion,
        string? loader, string? sourceId, CancellationToken ct, int limit, int offset)
    {
        var source = GetModpackSource(sourceId);
        if (!source.IsAvailable) return new List<ModpackItem>();
        return await source.SearchAsync(keyword, gameVersion, loader, limit, offset, ct);
    }

    /// <summary>获取整合包详情（含可安装版本列表）。</summary>
    public async Task<ModpackDetail?> GetModpackDetailAsync(string? sourceId, string id, CancellationToken ct)
    {
        var source = GetModpackSource(sourceId);
        if (!source.IsAvailable) return null;
        return await source.GetDetailAsync(id, ct);
    }

    /// <summary>
    /// 安装指定整合包（按 <paramref name="version"/> 直链下载后落地）。
    /// Modrinth .mrpack 支持<paramref name="isolated"/> 隔离目录。
    /// </summary>
    public async Task<ModpackInstallResult?> InstallModpackVersionAsync(
        string? sourceId, ModpackVersion version, bool isolated, string? preferredName,
        IProgress<double>? progress, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(version.FileUrl)) return null;

        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")
            + ".mrpack");
        try
        {
            await _downloader.DownloadAsync(new DownloadItem(new[] { version.FileUrl }, tmp, version.Sha1), progress, ct);

            var installer = new ModpackInstaller(GameRoot, _client, _downloader, this);
            return await installer.InstallAsync(tmp, isolated, preferredName, null, ct);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 便捷安装：按项目 Id 取详情并挑选最新（匹配 <paramref name="gameVersion"/> 优先）版本安装。
    /// 默认隔离安装（规格用户决策：独立版本隔离目录）。
    /// </summary>
    public async Task<ModpackInstallResult?> InstallModpackAsync(
        string? sourceId, string projectId, string? gameVersion, string? preferredName,
        IProgress<double>? progress, CancellationToken ct)
    {
        var detail = await GetModpackDetailAsync(sourceId, projectId, ct);
        if (detail is null || detail.Versions.Count == 0) return null;

        var version = detail.Versions.FirstOrDefault(v =>
                          string.IsNullOrEmpty(gameVersion) || string.Equals(v.GameVersion, gameVersion, StringComparison.OrdinalIgnoreCase))
                      ?? detail.Versions[0];

        return await InstallModpackVersionAsync(sourceId, version, isolated: true, preferredName, progress, ct);
    }

    public void Log(string message) => Logged?.Invoke(message);

    // ---- 版本列表 ----

    public async Task<List<string>> GetVanillaVersionsAsync()
    {
        try
        {
            var json = await MirrorPolicy.GetStringWithFallback(MirrorPolicy.VersionManifestUrls(), _client);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json);
            return manifest?.Versions.Select(v => v.Id).ToList() ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<(string Id, string Type)> ListInstalledVersions()
    {
        var result = new List<(string Id, string Type)>();
        var versionsDir = PathEx.VersionsDir(GameRoot);
        if (!Directory.Exists(versionsDir)) return result;

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = PathEx.VersionJsonPath(GameRoot, id);
            if (!File.Exists(jsonPath)) continue;
            var type = "";
            try
            {
                var json = File.ReadAllText(jsonPath);
                var v = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json);
                type = v?.Type ?? "";
            }
            catch { /* 忽略解析错误 */ }
            result.Add((id, type));
        }
        return result;
    }

    // ---- 安装 ----

    public async Task InstallAsync(string installType, string versionId,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        InstallerBase installer = installType.ToLowerInvariant() switch
        {
            "fabric" => new FabricInstaller(GameRoot, _client, _downloader, this),
            "forge" => new ForgeInstaller(GameRoot, _client, _downloader, this),
            "neoforge" => new NeoForgeInstaller(GameRoot, _client, _downloader, this),
            "quilt" => new QuiltInstaller(GameRoot, _client, _downloader, this),
            _ => new VanillaInstaller(GameRoot, _client, _downloader, this)
        };

        switch (installer)
        {
            case VanillaInstaller v: await v.InstallAsync(versionId, progress, ct); break;
            case FabricInstaller f: await f.InstallAsync(versionId, progress, ct); break;
            case ForgeInstaller g: await g.InstallAsync(versionId, progress, ct); break;
            case NeoForgeInstaller n: await n.InstallAsync(versionId, progress, ct); break;
            case QuiltInstaller q: await q.InstallAsync(versionId, progress, ct); break;
        }
    }

    // ---- 版本安装（下载页 → Minecraft 下载）----

    /// <summary>返回完整版本清单条目（含类型 / 发布时间），供下载页 Minecraft 子页列举与分类。</summary>
    public async Task<List<VersionEntry>> GetVanillaVersionsDetailedAsync()
    {
        try
        {
            var json = await MirrorPolicy.GetStringWithFallback(MirrorPolicy.VersionManifestUrls(), _client);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json);
            return manifest?.Versions ?? new List<VersionEntry>();
        }
        catch
        {
            return new List<VersionEntry>();
        }
    }

    /// <summary>
    /// 安装一个 Minecraft 版本（下载页 Minecraft 子页调用）。
    /// <paramref name="loader"/> 为 none / forge / fabric / neoforge / quilt；
    /// fabric 由 <see cref="FabricInstaller"/> 自动配对最新稳定 Fabric API。
    /// 返回安装得到的新版本 Id（原版返回 <paramref name="mcVersion"/>）。
    /// </summary>
    public async Task<string?> InstallVersionAsync(string mcVersion, string loader,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string? id;
        switch (loader.ToLowerInvariant())
        {
            case "fabric":
                id = await new FabricInstaller(GameRoot, _client, _downloader, this).InstallAsync(mcVersion, null, ct);
                break;
            case "forge":
                id = await new ForgeInstaller(GameRoot, _client, _downloader, this).InstallAsync(mcVersion, null, ct);
                break;
            case "neoforge":
                id = await new NeoForgeInstaller(GameRoot, _client, _downloader, this).InstallAsync(mcVersion, null, ct);
                break;
            case "quilt":
                id = await new QuiltInstaller(GameRoot, _client, _downloader, this).InstallAsync(mcVersion, null, ct);
                break;
            default:
                await new VanillaInstaller(GameRoot, _client, _downloader, this).InstallAsync(mcVersion, null, ct);
                id = mcVersion;
                break;
        }

        // 安装成功：通知各页面（游戏页快速启动下拉）刷新版本列表，使新下载的版本立即可见。
        if (!string.IsNullOrEmpty(id))
        {
            try { VersionInstalled?.Invoke(id); } catch { }
            RaiseVersionListChanged();
        }
        return id;
    }

    // ---- 启动 ----

    /// <summary>
    /// 使用指定认证方式启动游戏。authenticator 为 null 时默认离线。cliOverrides 可覆盖内存/用户名/Java。
    /// <para>
    /// 会叠加「每版本覆盖层」<c>versions/&lt;id&gt;/profile.json</c>（对齐 MCLCS-Linux）：
    /// 每版本 Java / 内存 / 分辨率 / 全屏 / 额外 JVM 参数 / 工作目录 / 绑定账号，未设置则回落全局。
    /// </para>
    /// </summary>
    public async Task<LaunchResult> LaunchAsync(string versionId,
        IAuthenticator? authenticator = null,
        LaunchCliOverrides? cliOverrides = null,
        CancellationToken ct = default)
    {
        var profile = ProfileStore.Load(GameRoot);
        var vp = VersionProfileStore.Load(GameRoot, versionId);
        var java = await ResolveJavaAsync(profile, vp, cliOverrides, versionId, ct);

        // 解析账号优先级：UI 显式选择 > 该版本绑定账号 > 全局「最后使用」
        var account = ResolveAccount(cliOverrides?.AccountId, vp.BoundAccountId);
        AuthSession session;
        if (authenticator is not null)
        {
            var username = cliOverrides?.Username ?? account?.Username ?? profile.DefaultUsername;
            session = await authenticator.AuthenticateAsync(username, ct);
        }
        else if (account is { } bound && !string.IsNullOrWhiteSpace(bound.Uuid)
                 && !string.IsNullOrWhiteSpace(bound.AccessToken))
        {
            // 已保存的账号会话（含每版本绑定）：直接复用，无需重新认证
            session = new AuthSession
            {
                Username = bound.Username,
                Uuid = bound.Uuid,
                AccessToken = bound.AccessToken,
                UserType = bound.AuthType == "microsoft" ? "msa" : bound.AuthType
            };
        }
        else
        {
            var username = cliOverrides?.Username ?? account?.Username ?? profile.DefaultUsername;
            session = await new OfflineAuthenticator().AuthenticateAsync(username, ct);
        }

        // 额外 JVM 参数：全局额外参数在前，每版本追加在后（可被其覆盖语义）
        var extraJvm = new List<string>(profile.ExtraJvmArgs);
        extraJvm.AddRange(vp.ExtraJvmArgs);
        if (cliOverrides?.ExtraJvmArgs is { Count: > 0 } cliJvm) extraJvm.AddRange(cliJvm);

        var options = new LaunchOptions
        {
            Username = session.Username,
            Uuid = session.Uuid,
            AccessToken = session.AccessToken,
            UserType = session.UserType,
            MaxMemoryMb = cliOverrides?.MaxMemoryMb ?? vp.MaxMemoryMb ?? profile.MaxMemoryMb,
            MinMemoryMb = cliOverrides?.MinMemoryMb ?? vp.MinMemoryMb ?? profile.MinMemoryMb,
            ExtraJvmArgs = extraJvm,
            ServerAddress = cliOverrides?.ServerAddress,
            Fullscreen = vp.Fullscreen,
            GameDir = ResolveGameDir(GameRoot, versionId, vp)
        };

        // 分辨率：每版本覆盖优先，否则用全局
        if (vp.ResolutionWidth is > 0 && vp.ResolutionHeight is > 0)
            options.Resolution = (vp.ResolutionWidth.Value, vp.ResolutionHeight.Value);
        else if (profile.ResolutionWidth.HasValue && profile.ResolutionHeight.HasValue)
            options.Resolution = (profile.ResolutionWidth.Value, profile.ResolutionHeight.Value);

        // 记住最后使用的版本
        profile.LastVersionId = versionId;
        ProfileStore.Save(profile);

        return await GameLauncher.LaunchAsync(GameRoot, versionId, java, options, this, ct);
    }

    /// <summary>
    /// 解析该版本的有效工作目录。仅当用户<b>已显式保存过</b>版本设置时才用覆盖层，
    /// 否则沿用 <see cref="VersionIsolation"/> 既有标记行为（返回 null 交给底层判定）。
    /// </summary>
    private static string? ResolveGameDir(string gameRoot, string versionId, VersionProfile vp) =>
        VersionProfileStore.HasProfile(gameRoot, versionId)
            ? VersionProfileStore.EffectiveGameDir(gameRoot, versionId, vp)
            : null;

    /// <summary>
    /// 解析启动应使用的账号：UI 显式选择 &gt; 每版本绑定 &gt; 全局「最后使用」。
    /// 显式选择的账号若已被删除（Id 找不到），静默回落到每版本绑定逻辑。
    /// </summary>
    private AccountEntry? ResolveAccount(string? explicitAccountId, string? boundAccountId)
    {
        if (!string.IsNullOrWhiteSpace(explicitAccountId))
        {
            var picked = AccountStore.Load(GameRoot).FirstOrDefault(a => a.Id == explicitAccountId);
            if (picked is not null) return picked;
        }
        return AccountStore.GetForVersion(GameRoot, boundAccountId);
    }

    /// <summary>
    /// 执行一次崩溃自动修复。所有修复均为非破坏性：
    /// 调大内存仅改启动器配置、切换 Java 仅影响外部 Java、重下库仅重写依赖缓存、
    /// 禁用冲突 Mod 仅重命名为 .disabled（可还原）、安装缺失前置仅向 mods 目录新增文件。
    /// 返回修复是否成功执行。
    /// </summary>
    public async Task<bool> ApplyRepairAsync(CrashRepairPlan plan, CancellationToken ct = default)
    {
        var profile = ProfileStore.Load(GameRoot);

        switch (plan.Strategy)
        {
            case RepairStrategy.IncreaseMemory:
                if (plan.TargetMemoryMb is not null)
                {
                    profile.MaxMemoryMb = plan.TargetMemoryMb.Value;
                    ProfileStore.Save(profile);
                    Log($"自动修复：内存调整至 {plan.TargetMemoryMb.Value}MB");
                    return true;
                }
                return false;

            case RepairStrategy.SwitchJava:
            {
                // 优先用崩溃方案给出的要求；否则按该版本自身的 Java 需求推断（1.16-→8，1.21+→21）
                var required = plan.RequiredJavaMajor
                    ?? (string.IsNullOrEmpty(plan.VersionId)
                        ? GameConstants.MinimumJavaMajorVersion
                        : JavaDetector.RequiredMajorForVersionId(GameRoot, plan.VersionId));
                var java = await JavaDetector.FindBestAsync(required);
                if (java is null)
                {
                    Log($"未找到 Java {required}+，尝试下载安装（{profile.PreferredJavaVendor}）…");
                    java = await JavaInstaller.EnsureJavaAsync(required, GameRoot, _downloader, profile.PreferredJavaVendor, this, ct);
                }
                if (java is null)
                {
                    Log($"自动修复失败：无法获取 Java {required}+");
                    return false;
                }
                profile.JavaPath = java.JavaExe;
                ProfileStore.Save(profile);
                Log($"自动修复：切换 Java 至 {java}");
                return true;
            }

            case RepairStrategy.RedownloadLibraries:
                if (string.IsNullOrEmpty(plan.VersionId)) return false;
                var repair = await LibraryRepair.RepairAsync(GameRoot, plan.VersionId, _client, _downloader, this, ct);
                return repair.Success || repair.AllHealthy;

            case RepairStrategy.DisableConflictingMods:
                return ApplyDisableConflictingMods(plan);

            case RepairStrategy.InstallMissingModDependency:
                return await ApplyInstallMissingModsAsync(plan, ct);

            case RepairStrategy.ResetResourcePacks:
            {
                var r = MCLCS.Core.Resources.ResourcePackRepairer.ResetToVanilla(GameRoot);
                foreach (var a in r.Actions) Log(a);
                if (!r.Success) Log($"资源包/光影回滚失败：{r.Error}");
                return r.Success;
            }

            // §四.2 降级联动
            case RepairStrategy.RevertDowngradeBackup:
            case RepairStrategy.RetryDowngradeOtherMethod:
            case RepairStrategy.InstallOriginalVersion:
                return await ApplyDowngradeRecoveryAsync(plan, ct);

            case RepairStrategy.None:
            default:
                return false;
        }
    }

    /// <summary>禁用冲突 Mod：保留用户选定的一个，其余重命名为 .disabled（不删除）。</summary>
    private bool ApplyDisableConflictingMods(CrashRepairPlan plan)
    {
        bool any = false;
        foreach (var mod in plan.ConflictingMods)
        {
            if (string.IsNullOrEmpty(mod.FilePath)) continue;
            if (string.Equals(mod.FilePath, plan.KeepModFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (!File.Exists(mod.FilePath)) continue;
            if (mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;

            var disabled = mod.FilePath + ".disabled";
            try
            {
                if (File.Exists(disabled))
                {
                    // 已存在 .disabled 副本，直接移除启用的那份
                    File.Delete(mod.FilePath);
                }
                else
                {
                    File.Move(mod.FilePath, disabled);
                }
                Log($"禁用冲突 Mod：{Path.GetFileName(mod.FilePath)} → {Path.GetFileName(disabled)}");
                any = true;
            }
            catch (Exception ex)
            {
                Log($"禁用 Mod 失败 {mod.FilePath}：{ex.Message}");
            }
        }
        return any || plan.ConflictingMods.Count > 0;
    }

    /// <summary>自动安装缺失的 Mod 前置依赖（从 Modrinth 下载到 mods 目录）。</summary>
    private async Task<bool> ApplyInstallMissingModsAsync(CrashRepairPlan plan, CancellationToken ct)
    {
        if (plan.MissingModDependencies.Count == 0) return false;

        var loader = DetectLoader(GameRoot, plan.VersionId);
        var gameVersion = ExtractGameVersion(GameRoot, plan.VersionId);
        var client = new ModrinthClient(_client);

        var allOk = true;
        foreach (var id in plan.MissingModDependencies)
        {
            try
            {
                var ok = await InstallModDependencyAsync(client, id, loader, gameVersion, ct);
                if (ok) Log($"已安装缺失前置：{id}");
                else { Log($"未找到可安装的缺失前置：{id}"); allOk = false; }
            }
            catch (Exception ex)
            {
                Log($"安装缺失前置失败 {id}：{ex.Message}");
                allOk = false;
            }
        }
        return allOk;
    }

    /// <summary>§四.2 降级联动恢复：回滚备份 / 改用 Amulet 重试 / 安装存档原版本。</summary>
    private async Task<bool> ApplyDowngradeRecoveryAsync(CrashRepairPlan plan, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(plan.SavePath))
        {
            Log("降级联动恢复失败：未指定存档路径。");
            return false;
        }

        switch (plan.Strategy)
        {
            case RepairStrategy.RevertDowngradeBackup:
            {
                if (string.IsNullOrEmpty(plan.BackupPath) || !Directory.Exists(plan.BackupPath))
                {
                    Log("回滚失败：找不到降级备份。");
                    return false;
                }
                var replaced = SaveDowngrader.RestoreBackupAsync(plan.BackupPath, plan.SavePath);
                Log($"已回滚到降级前备份（当前存档另存于 {replaced}）。");
                return true;
            }

            case RepairStrategy.RetryDowngradeOtherMethod:
            {
                if (string.IsNullOrEmpty(plan.BackupPath) || !Directory.Exists(plan.BackupPath))
                {
                    Log("改用其他方式失败：找不到降级备份。");
                    return false;
                }
                // 以当前存档的目标 DataVersion 为基准，先回滚到干净备份，再用 Amulet 重做降级
                var targetDv = SaveDowngrader.GetSaveDataVersion(plan.SavePath);
                var targetVer = DataVersionMap.ToGameVersion(targetDv);
                if (targetVer is null)
                {
                    Log($"改用其他方式失败：目标 DataVersion {targetDv} 不在对照表中。");
                    return false;
                }
                SaveDowngrader.RestoreBackupAsync(plan.BackupPath, plan.SavePath);
                var dp = await SaveDowngrader.DowngradeAsync(plan.SavePath, targetVer, DowngradeMethod.Amulet);
                if (dp.Success) Log($"已用 Amulet 重新降级到 {targetVer}。");
                else Log($"改用 Amulet 降级失败：{dp.ErrorMessage}");
                return dp.Success;
            }

            case RepairStrategy.InstallOriginalVersion:
            {
                if (string.IsNullOrEmpty(plan.VersionId))
                {
                    Log("安装原版本失败：未记录原版本号。");
                    return false;
                }
                try
                {
                    var installType = DetectInstallType(plan.VersionId);
                    Log($"正在安装存档原版本 {plan.VersionId}（{installType}）…");
                    await InstallAsync(installType, plan.VersionId, ct: ct);
                    // 安装后把"最后使用版本"指向原版本，使后续启动用正确版本打开存档
                    var p = ProfileStore.Load(GameRoot);
                    p.LastVersionId = plan.VersionId;
                    ProfileStore.Save(p);
                    Log($"已安装原版本 {plan.VersionId}，将用该版本打开存档。");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"安装原版本失败：{ex.Message}（可手动在「安装新版本」中安装 {plan.VersionId}）");
                    return false;
                }
            }

            default:
                return false;
        }
    }

    /// <summary>由版本 id 前缀推断安装类型（vanilla / fabric / forge / quilt / neoforge）。</summary>
    private static string DetectInstallType(string versionId)
    {
        var v = versionId.ToLowerInvariant();
        if (v.StartsWith("neoforge")) return "neoforge";
        if (v.StartsWith("fabric")) return "fabric";
        if (v.StartsWith("forge")) return "forge";
        if (v.StartsWith("quilt")) return "quilt";
        return "vanilla";
    }

    private async Task<bool> InstallModDependencyAsync(ModrinthClient client, string modId,
        LoaderType loader, string? gameVersion, CancellationToken ct)
    {
        var search = await client.SearchAsync(modId, gameVersion, loader, ModrinthProjectType.Mod, limit: 5, ct: ct);
        var hit = search.Hits.FirstOrDefault(h => string.Equals(h.Slug, modId, StringComparison.OrdinalIgnoreCase))
                  ?? search.Hits.FirstOrDefault();
        if (hit is null) return false;

        var versions = await client.GetVersionsAsync(hit.ProjectId, ct);
        var ver = versions.FirstOrDefault(v =>
                        (gameVersion is null || v.GameVersions.Contains(gameVersion))
                        && (loader == LoaderType.Any || v.Loaders.Contains(ModrinthClient.LoaderString(loader), StringComparer.OrdinalIgnoreCase)))
                  ?? versions.FirstOrDefault();
        if (ver is null) return false;

        var file = client.SelectBestFile(ver, gameVersion, loader);
        if (file is null) return false;

        var modsDir = PathEx.ModsDir(GameRoot);
        Directory.CreateDirectory(modsDir);
        var dest = Path.Combine(modsDir, file.FileName);
        await _downloader.DownloadAsync(new DownloadItem(new[] { file.Url }, dest, file.Hashes.Sha1), null, ct);
        return true;
    }

    /// <summary>从版本合并结果推断加载器类型。</summary>
    private static LoaderType DetectLoader(string gameRoot, string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return LoaderType.Any;
        try
        {
            var merged = VersionMerger.Merge(gameRoot, versionId);
            var mc = merged.MainClass ?? "";
            if (mc.Contains("fabricmc", StringComparison.OrdinalIgnoreCase)) return LoaderType.Fabric;
            if (mc.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return LoaderType.NeoForge;
            if (mc.Contains("forge", StringComparison.OrdinalIgnoreCase)) return LoaderType.Forge;
            if (mc.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return LoaderType.Quilt;
        }
        catch { /* 忽略 */ }
        return LoaderType.Any;
    }

    /// <summary>从版本 id（如 fabric-1.20.1）中提取 Minecraft 游戏版本号。</summary>
    private static string? ExtractGameVersion(string gameRoot, string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return null;
        var m = Regex.Match(versionId, @"\d+\.\d+(?:\.\d+)?");
        return m.Success ? m.Value : null;
    }

    /// <summary>
    /// 解析启动该版本应使用的 Java（对齐 MCLCS-Linux 的按版本智能选版）：
    /// 1. 显式路径优先（CLI &gt; 每版本覆盖 &gt; 全局），但必须满足该版本所需主版本；
    /// 2. 否则在已检测的 Java 里挑「满足要求且尽可能低」的（老 MC/Forge 常不兼容过高 Java）；
    /// 3. 本地没有满足要求的才尝试下载安装，仍失败则用最高版本兜底。
    /// </summary>
    private async Task<JavaInfo> ResolveJavaAsync(LauncherProfile profile, VersionProfile vp,
        LaunchCliOverrides? cliOverrides, string versionId, CancellationToken ct)
    {
        var required = JavaDetector.RequiredMajorForVersionId(GameRoot, versionId);
        var explicitPath = cliOverrides?.JavaPath ?? vp.JavaPath ?? profile.JavaPath;

        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
        {
            var (major, raw) = await JavaDetector.QueryVersionAsync(explicitPath);
            if (major >= required)
                return new JavaInfo { JavaExe = explicitPath, MajorVersion = major, RawVersion = raw };
        }

        var detected = await JavaDetector.DetectAsync();
        if (detected.Count > 0)
        {
            var picked = JavaDetector.SelectForVersion(detected, GameRoot, versionId, explicitPath);
            if (picked is not null && picked.MajorVersion >= required) return picked;
        }

        // 本地没有满足要求的 Java：尝试下载安装该版本所需主版本
        var java = await JavaInstaller.EnsureJavaAsync(required, GameRoot, _downloader, profile.PreferredJavaVendor, this, ct);
        return java
            ?? (detected.Count > 0 ? detected.OrderByDescending(j => j.MajorVersion).First() : null)
            ?? throw new InvalidOperationException("未找到可用的 Java 运行环境");
    }

    // ---- 下载中心（Modrinth）----

    public async Task<List<ModrinthHit>> SearchModsAsync(string query, string? gameVersion, LoaderType loader, ModrinthProjectType type)
    {
        var client = new ModrinthClient(_client);
        var result = await client.SearchAsync(query, gameVersion, loader, type);
        return result.Hits;
    }

    /// <summary>
    /// 分页搜索 Modrinth 项目（bug #16：mod / 光影 / 资源包此前一次性只取前 25 条且无法翻页）。
    /// 返回本页条目与远端总命中数，供 UI 计算总页数。
    /// </summary>
    public async Task<(List<ModrinthHit> Hits, int TotalHits)> SearchModsPagedAsync(
        string? query, string? gameVersion, LoaderType loader, ModrinthProjectType type,
        int limit, int offset, CancellationToken ct = default)
    {
        var client = new ModrinthClient(_client);
        var result = await client.SearchAsync(query, gameVersion, loader, type, limit, offset, null, ct);
        return (result.Hits, result.TotalHits);
    }

    public async Task<bool> DownloadModAsync(string projectId, string targetDir, string? gameVersion, LoaderType loader)
    {
        var client = new ModrinthClient(_client);
        var versions = await client.GetVersionsAsync(projectId);
        var ver = versions.FirstOrDefault(v =>
                        (string.IsNullOrEmpty(gameVersion) || v.GameVersions.Contains(gameVersion))
                        && (loader == LoaderType.Any || v.Loaders.Contains(ModrinthClient.LoaderString(loader), StringComparer.OrdinalIgnoreCase)))
                  ?? versions.FirstOrDefault();
        if (ver is null) return false;

        var file = client.SelectBestFile(ver, gameVersion, loader);
        if (file is null) return false;

        Directory.CreateDirectory(targetDir);
        var dest = Path.Combine(targetDir, file.FileName);
        await _downloader.DownloadAsync(new DownloadItem(new[] { file.Url }, dest, file.Hashes.Sha1), null, CancellationToken.None);
        return true;
    }

    /// <summary>
    /// bug #14：拉取项目版本列表（含每个版本的最佳文件直链），供 mod / 光影 / 资源包详情页选择版本。
    /// 按当前筛选的游戏版本与加载器挑文件；无可用文件的版本会被跳过。
    /// </summary>
    public async Task<List<ProjectVersionChoice>> GetProjectVersionChoicesAsync(
        string projectId, string? gameVersion, LoaderType loader, CancellationToken ct = default)
    {
        var client = new ModrinthClient(_client);
        var versions = await client.GetVersionsAsync(projectId, ct);
        var list = new List<ProjectVersionChoice>();
        foreach (var v in versions)
        {
            var file = client.SelectBestFile(v, gameVersion, loader);
            if (file is null) continue;
            list.Add(new ProjectVersionChoice
            {
                Id = v.Id,
                Name = v.Name,
                VersionNumber = v.VersionNumber,
                GameVersionSummary = v.GameVersions.Count > 0 ? string.Join(", ", v.GameVersions.Take(4)) : "-",
                LoaderSummary = v.Loaders.Count > 0 ? string.Join(", ", v.Loaders) : "-",
                FileUrl = file.Url,
                FileName = file.FileName,
                FileSha1 = file.Hashes.Sha1
            });
        }
        return list;
    }

    /// <summary>
    /// bug #14：按指定文件直链下载（详情页选择了具体版本时使用）。
    /// 与 <see cref="DownloadModAsync"/> 的区别是不再自动挑版本，用户选哪个就装哪个。
    /// </summary>
    public async Task<bool> DownloadModFileAsync(string fileUrl, string fileName, string? sha1,
        string targetDir, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var dest = Path.Combine(targetDir, fileName);
        await _downloader.DownloadAsync(new DownloadItem(new[] { fileUrl }, dest, sha1), progress, ct);
        return true;
    }

    /// <summary>带进度与取消的 Mod 下载（供下载队列使用）。</summary>
    public async Task<bool> DownloadModAsync(string projectId, string targetDir, string? gameVersion,
        LoaderType loader, IProgress<double>? progress, CancellationToken ct)
    {
        var client = new ModrinthClient(_client);
        var versions = await client.GetVersionsAsync(projectId);
        var ver = versions.FirstOrDefault(v =>
                        (string.IsNullOrEmpty(gameVersion) || v.GameVersions.Contains(gameVersion))
                        && (loader == LoaderType.Any || v.Loaders.Contains(ModrinthClient.LoaderString(loader), StringComparer.OrdinalIgnoreCase)))
                  ?? versions.FirstOrDefault();
        if (ver is null) return false;

        var file = client.SelectBestFile(ver, gameVersion, loader);
        if (file is null) return false;

        Directory.CreateDirectory(targetDir);
        var dest = Path.Combine(targetDir, file.FileName);
        await _downloader.DownloadAsync(new DownloadItem(new[] { file.Url }, dest, file.Hashes.Sha1), progress, ct);
        return true;
    }

    // ---- 下载页：地图（像素茶艺）----

    /// <summary>下载并安装地图：先取详情直链，下载压缩包，再用 MapInstaller 解压进 saves。</summary>
    public async Task<bool> DownloadMapAsync(string slug, IProgress<double>? progress, CancellationToken ct)
    {
        var detail = await Pixelmap.GetDetailAsync(slug, ct);
        if (detail is null || !detail.CanDownload) return false;

        var item = PixelmapClient.ToDownloadItem(detail, GameRoot);
        if (item is null) return false;

        await _downloader.DownloadAsync(item, progress, ct);
        var result = MapInstaller.Install(item.Destination, GameRoot);
        return result.Ok;
    }

    /// <summary>
    /// 下载地图的附加资源（资源包 / 光影）并智能分发到 resourcepacks / shaderpacks。
    /// 详情不含附加资源直链时返回 null。
    /// </summary>
    public async Task<ExtraResourceInstallResult?> DownloadMapExtraAsync(
        PixelMapDetail detail, IProgress<double>? progress, CancellationToken ct)
    {
        var item = PixelmapClient.ToExtraDownloadItem(detail, GameRoot);
        if (item is null) return null;

        await _downloader.DownloadAsync(item, progress, ct);
        return ExtraResourceInstaller.Install(item.Destination, GameRoot, detail.Title);
    }

    // ---- 下载页：整合包（Modrinth .mrpack）----
    // 安装入口见 InstallModpackAsync / InstallModpackVersionAsync（源无关、支持隔离安装）。
}

/// <summary>CLI 传入的启动参数覆盖（不持久化）。</summary>
public class LaunchCliOverrides
{
    public string? Username { get; set; }
    public int? MaxMemoryMb { get; set; }
    public int? MinMemoryMb { get; set; }
    public string? JavaPath { get; set; }
    public List<string>? ExtraJvmArgs { get; set; }
    /// <summary>
    /// 显式指定使用的账号 Id（用户在游戏页下拉里手动选择时传入）。
    /// 优先级高于「每版本绑定账号」与「全局最后使用」；为空时回落到每版本绑定逻辑。
    /// </summary>
    public string? AccountId { get; set; }
    /// <summary>直接连入服务器地址（host:port），启动后跳过主菜单自动加入。</summary>
    public string? ServerAddress { get; set; }
}
