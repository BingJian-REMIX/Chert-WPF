using System.Reflection;
using System.Text.Json;
using Chert.App.Services;
using Chert.Core.Mods;
using Chert.Core.Profiles;
using Chert.Core.Toolbox;
using Chert.Core.Utils;

namespace mclcs;

/// <summary>
/// 清单 #71：CLI 功能完善。
/// 统一参数解析（--key value / --key=value / 布尔开关 / 位置参数）、
/// 全局开关（--json / --quiet / --game-dir）、结构化输出与更完整的命令集，
/// 便于脚本调用与自动化。
/// </summary>
internal static class Program
{
    private static bool _json;
    private static bool _quiet;
    private static string _gameRoot = GameConstants.DefaultGameRoot;

    // CurseForge 命令行覆盖（优先级高于 profile 与内置 Key）
    private static string? _cfKey;
    private static string? _cfRoot;

    private static async Task<int> Main(string[] args)
    {
        var rest = ExtractGlobalFlags(args);

        // CurseForge 三层 Key 策略：先同步 profile（用户 Key / API Root / 开关），
        // 再叠加命令行覆盖 —— 命令行 > 用户设置 > 构建时注入的内置 Key。
        try { Chert.Core.Download.CurseForgeConfig.Apply(ProfileStore.Load(_gameRoot).CurseForge); }
        catch { /* 读配置失败按未配置处理，不阻塞命令 */ }
        if (_cfKey is not null) Chert.Core.Download.CurseForgeConfig.LaunchArgumentOverride = _cfKey;
        if (_cfRoot is not null) Chert.Core.Download.CurseForgeConfig.ApiRoot = _cfRoot;

        if (rest.Length == 0)
        {
            PrintHelp(null);
            return 0;
        }

        var cmd = rest[0].ToLowerInvariant();
        var sub = rest[1..];

        return cmd switch
        {
            "launch" => await Launch(sub),
            "list" or "versions" => ListVersions(sub),
            "install" => await Install(sub),
            "modpack" => await Modpack(sub),
            "mods" => await Mods(sub),
            "skin" => await Skin(sub),
            "dirs" => Dirs(sub),
            "profiles" or "config" => Profiles(sub),
            "update" or "self-update" => await SelfUpdate(sub),
            "logs" => Logs(sub),
            "completion" => Completion(sub),
            "version" or "--version" => Version(sub),
            "help" or "--help" or "-h" => Help(sub),
            _ => UnknownCommand(cmd)
        };
    }

    // ---------------- 全局开关与输出 ----------------

    private static string[] ExtractGlobalFlags(string[] args)
    {
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a is "--json") { _json = true; continue; }
            if (a is "--quiet" or "-q") { _quiet = true; continue; }
            if (a is "--game-dir" or "-g")
            {
                if (i + 1 < args.Length) _gameRoot = args[++i];
                continue;
            }
            if (a.StartsWith("--game-dir=", StringComparison.Ordinal))
            {
                _gameRoot = a["--game-dir=".Length..];
                continue;
            }
            if (a is "--curseforge-key")
            {
                if (i + 1 < args.Length) _cfKey = args[++i];
                continue;
            }
            if (a.StartsWith("--curseforge-key=", StringComparison.Ordinal))
            {
                _cfKey = a["--curseforge-key=".Length..];
                continue;
            }
            if (a is "--curseforge-root")
            {
                if (i + 1 < args.Length) _cfRoot = args[++i];
                continue;
            }
            if (a.StartsWith("--curseforge-root=", StringComparison.Ordinal))
            {
                _cfRoot = a["--curseforge-root=".Length..];
                continue;
            }
            rest.Add(a);
        }
        return rest.ToArray();
    }

    private static void Info(string msg)
    {
        if (!_quiet && !_json) Console.WriteLine(msg);
    }

    private static void Err(string msg) => Console.Error.WriteLine(msg);

    private static void WriteJson(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { WriteIndented = true }));

    private static int UnknownCommand(string cmd)
    {
        Err($"未知命令: {cmd}。运行 chert help 查看帮助。");
        return 1;
    }

    private static int Help(string[] args)
    {
        PrintHelp(args.Length > 0 ? args[0].ToLowerInvariant() : null);
        return 0;
    }

    // ---------------- launch ----------------

    private static async Task<int> Launch(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        var versionId = o.Positional.Count > 0 ? o.Positional[0] : null;

        if (string.IsNullOrEmpty(versionId))
        {
            Err("用法: chert launch <versionId> [--username <name>] [--memory <MB>] [--java <path>] [--game-dir <path>]");
            return 1;
        }

        int? memory = null;
        if (int.TryParse(o.Get("memory", "m"), out var m)) memory = m;

        var svc = new LauncherService(_gameRoot);
        svc.Logged += msg => Info($"[Chert] {msg}");

        Info($"启动版本: {versionId}");
        try
        {
            var overrides = new LaunchCliOverrides
            {
                Username = o.Get("username", "user", "u"),
                MaxMemoryMb = memory,
                JavaPath = o.Get("java")
            };
            var result = await svc.LaunchAsync(versionId, cliOverrides: overrides);

            if (_json)
            {
                WriteJson(new
                {
                    versionId,
                    exitCode = result.ExitCode,
                    crashed = result.CrashReportPath is not null,
                    crashReportPath = result.CrashReportPath
                });
                return result.ExitCode;
            }

            if (result.CrashReportPath is not null)
                Info($"检测到崩溃报告: {result.CrashReportPath} (退出码 {result.ExitCode})");
            else
                Info($"游戏正常退出 (退出码 {result.ExitCode})");
            return result.ExitCode;
        }
        catch (Exception ex)
        {
            Err($"启动失败: {ex.Message}");
            return 1;
        }
    }

    // ---------------- list / versions ----------------

    private static int ListVersions(string[] args)
    {
        CliOptions.Parse(args, ValueKeys);
        var svc = new LauncherService(_gameRoot);
        var versions = svc.ListInstalledVersions();

        if (_json)
        {
            WriteJson(new
            {
                gameRoot = _gameRoot,
                count = versions.Count,
                versions = versions.Select(v => new { id = v.Item1, type = v.Item2 })
            });
            return 0;
        }

        if (versions.Count == 0)
        {
            Info("暂无已安装版本。使用 chert install <vanilla|fabric|forge> <version> 安装。");
            return 0;
        }

        Info($"已安装版本 ({_gameRoot})：");
        foreach (var (id, type) in versions)
            Info($"  {id,-30} {type}");
        return 0;
    }

    // ---------------- install ----------------

    private static async Task<int> Install(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        if (o.Positional.Count < 2)
        {
            Err("用法: chert install <vanilla|fabric|forge> <versionId> [--game-dir path]");
            return 1;
        }

        var installType = o.Positional[0].ToLowerInvariant();
        var versionId = o.Positional[1];

        if (installType is not ("vanilla" or "fabric" or "forge"))
        {
            Err("类型必须是 vanilla、fabric 或 forge");
            return 1;
        }

        var svc = new LauncherService(_gameRoot);
        svc.Logged += msg => Info($"[Chert] {msg}");

        Info($"安装 {installType} {versionId} → {_gameRoot}");
        try
        {
            await svc.InstallAsync(installType, versionId);
            if (_json) WriteJson(new { installed = true, type = installType, versionId, gameRoot = _gameRoot });
            else Info("安装完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Err($"安装失败: {ex.Message}");
            return 1;
        }
    }

    // ---------------- modpack ----------------

    private static async Task<int> Modpack(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        if (o.Positional.Count < 2)
        {
            Err("用法: chert modpack <modrinth|curseforge|auto> <文件路径> [--curseforge-key key] [--game-dir path]");
            return 1;
        }

        var packType = o.Positional[0].ToLowerInvariant();
        var filePath = o.Positional[1];

        if (packType is not ("modrinth" or "curseforge" or "auto"))
        {
            Err("整合包类型支持 modrinth / curseforge / auto（auto = 按内容自动识别）");
            return 1;
        }

        if (!File.Exists(filePath))
        {
            Err($"文件不存在: {filePath}");
            return 1;
        }

        if (packType == "curseforge" && !Chert.Core.Download.CurseForgeConfig.HasKey)
        {
            Err("安装 CurseForge 整合包需要 API Key：用 --curseforge-key <key> 传入，"
                + "或在启动器「设置 → 下载」中配置。");
            return 1;
        }

        Info($"安装 {packType} 整合包: {filePath} → {_gameRoot}");
        try
        {
            // 必须用带 CurseForgeAuthHandler 的 HttpClient —— 2026-07 起 CF CDN 直链强制带 x-api-key
            var client = new HttpClient(new Chert.Core.Download.CurseForgeAuthHandler(new HttpClientHandler()));
            var installer = new Chert.Core.Installers.ModpackInstaller(
                _gameRoot, client, new Chert.Core.Download.HttpDownloader(client),
                new CliLogger());

            if (packType == "curseforge") await installer.InstallCurseForgeAsync(filePath);
            else await installer.InstallAnyAsync(filePath);
            if (_json) WriteJson(new { installed = true, file = filePath, gameRoot = _gameRoot });
            else Info("整合包安装完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Err($"安装失败: {ex.Message}");
            return 1;
        }
    }

    // ---------------- mods ----------------

    private static async Task<int> Mods(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        var subCmd = o.Positional.Count > 0 ? o.Positional[0].ToLowerInvariant() : "list";
        var manager = new ModManager(_gameRoot, new HttpClient(), new Chert.Core.Download.HttpDownloader(new HttpClient()));

        switch (subCmd)
        {
            case "list":
            {
                var mods = manager.ListInstalledMods(includeDisabled: true);
                if (_json)
                {
                    WriteJson(new
                    {
                        gameRoot = _gameRoot,
                        count = mods.Count,
                        mods = mods.Select(m => new
                        {
                            name = m.Name,
                            modId = m.ModId,
                            file = m.FileName,
                            version = m.InstalledVersion,
                            latest = m.LatestVersion,
                            loader = m.Loader,
                            enabled = m.Enabled,
                            hasUpdate = m.HasUpdate
                        })
                    });
                    return 0;
                }
                Info($"已安装 Mod ({mods.Count})：");
                foreach (var m in mods)
                    Info($"  {(m.Enabled ? "  " : "[禁]")} {m.Name,-36} v{m.InstalledVersion,-12} [{m.Loader}]");
                return 0;
            }

            case "check" or "deps":
            {
                var results = manager.CheckDependencies();
                if (_json)
                {
                    WriteJson(new
                    {
                        gameRoot = _gameRoot,
                        problems = results.Count,
                        items = results.Select(r => new
                        {
                            mod = r.ModName,
                            modId = r.ModId,
                            version = r.ModVersion,
                            missing = r.Missing.Select(d => new { id = d.DependencyId, range = d.VersionRange, required = d.Required }),
                            conflicts = r.Conflicts.Select(c => new { id = c.ConflictId, installed = c.InstalledVersion, range = c.ConflictRange })
                        })
                    });
                    return 0;
                }
                if (results.Count == 0)
                {
                    Info("所有依赖已满足。");
                    return 0;
                }
                Info($"依赖问题 ({results.Count})：");
                foreach (var r in results)
                {
                    Info($"  {r.ModName} ({r.ModId} v{r.ModVersion}):");
                    foreach (var dep in r.Missing)
                        Info($"    缺失: {dep.DependencyId} ({dep.VersionRange}) [必需]");
                    foreach (var c in r.Conflicts)
                        Info($"    冲突: {c.ConflictId} (已安装 {c.InstalledVersion}, 冲突 {c.ConflictRange})");
                }
                return 0;
            }

            case "updates":
            {
                Info("检查更新中...");
                var mods = await manager.CheckForUpdatesAsync();
                var hasUpdate = mods.Where(m => m.HasUpdate).ToList();
                if (_json)
                {
                    WriteJson(new
                    {
                        gameRoot = _gameRoot,
                        total = mods.Count,
                        outdated = hasUpdate.Count,
                        items = hasUpdate.Select(m => new
                        {
                            name = m.Name,
                            from = m.InstalledVersion,
                            to = m.LatestVersion,
                            url = m.ProjectUrl
                        })
                    });
                    return 0;
                }
                Info($"检查完成。{hasUpdate.Count}/{mods.Count} 个 Mod 有新版本：");
                foreach (var m in hasUpdate)
                    Info($"  {m.Name}: {m.InstalledVersion} → {m.LatestVersion}  {m.ProjectUrl}");
                return 0;
            }

            // 清单 #66：核验
            case "verify":
            {
                var targets = ResolveModTargets(manager, o);
                if (targets.Count == 0) { Err("没有匹配到 Mod。用 --all 核验全部，或 --file <文件名> 指定。"); return 1; }

                var results = targets.Select(m => manager.VerifyMod(m.FileName)).ToList();
                var bad = results.Count(r => !r.Ok);
                if (_json)
                {
                    WriteJson(new
                    {
                        gameRoot = _gameRoot,
                        total = results.Count,
                        failed = bad,
                        items = results.Select(r => new
                        {
                            file = r.FileName,
                            ok = r.Ok,
                            level = r.Level,
                            exists = r.Exists,
                            sizeBytes = r.SizeBytes,
                            validJar = r.IsValidJar,
                            hasMetadata = r.HasMetadata,
                            sha1 = r.Sha1,
                            message = r.Message
                        })
                    });
                }
                else
                {
                    foreach (var r in results)
                        Info($"  [{r.Level,-5}] {r.FileName,-40} {r.Message}");
                    Info(bad == 0 ? $"核验通过：{results.Count}/{results.Count}" : $"核验完成：{bad}/{results.Count} 个异常");
                }
                return bad == 0 ? 0 : 2;
            }

            // 清单 #66：一键更新
            case "update" or "upgrade":
            {
                var targets = ResolveModTargets(manager, o).Where(m => m.HasUpdate).ToList();
                if (targets.Count == 0)
                {
                    // 未显式指定时先跑一次在线检查
                    var all = await manager.CheckForUpdatesAsync();
                    targets = ResolveModTargets(manager, o, all).Where(m => m.HasUpdate).ToList();
                }
                if (targets.Count == 0)
                {
                    if (_json) WriteJson(new { updated = 0, failed = 0, items = Array.Empty<object>() });
                    else Info("没有需要更新的 Mod。");
                    return 0;
                }

                var ok = 0;
                var items = new List<object>();
                foreach (var m in targets)
                {
                    Info($"更新 {m.Name} ...");
                    var outcome = await manager.UpdateModAsync(m);
                    if (outcome.Success) ok++;
                    items.Add(new { name = m.Name, file = m.FileName, success = outcome.Success, message = outcome.Message, newFile = outcome.NewFileName });
                    if (!_json) Info($"  {(outcome.Success ? "OK  " : "FAIL")} {m.Name}: {outcome.Message}");
                }
                if (_json) WriteJson(new { total = targets.Count, updated = ok, failed = targets.Count - ok, items });
                else Info($"更新完成：{ok}/{targets.Count} 成功");
                return ok == targets.Count ? 0 : 2;
            }

            // 清单 #66：批量启用 / 禁用
            case "enable" or "disable":
            {
                var enable = subCmd == "enable";
                var targets = ResolveModTargets(manager, o).Where(m => m.Enabled != enable).ToList();
                if (targets.Count == 0)
                {
                    if (_json) WriteJson(new { changed = 0 });
                    else Info("没有需要切换状态的 Mod。");
                    return 0;
                }
                var ok = 0;
                foreach (var m in targets)
                    if (manager.SetModEnabled(m.FileName, enable) is not null) ok++;
                if (_json) WriteJson(new { total = targets.Count, changed = ok, enabled = enable });
                else Info($"已{(enable ? "启用" : "禁用")} {ok}/{targets.Count} 个 Mod");
                return ok == targets.Count ? 0 : 2;
            }

            // 清单 #66：卸载
            case "remove" or "uninstall":
            {
                var targets = ResolveModTargets(manager, o);
                if (targets.Count == 0) { Err("没有匹配到 Mod。用 --file <文件名> 指定，或 --all 卸载全部。"); return 1; }
                if (targets.Count > 1 && !o.Has("yes", "y"))
                {
                    Err($"即将卸载 {targets.Count} 个 Mod，请追加 --yes 确认。");
                    return 1;
                }
                var ok = 0;
                foreach (var m in targets)
                    if (manager.UninstallMod(m.FileName)) ok++;
                if (_json) WriteJson(new { total = targets.Count, removed = ok });
                else Info($"已卸载 {ok}/{targets.Count} 个 Mod");
                return ok == targets.Count ? 0 : 2;
            }

            default:
                Err("用法: chert mods <list|check|updates|verify|update|enable|disable|remove> [--all|--file <名>] [--json]");
                return 1;
        }
    }

    private static List<ModEntry> ResolveModTargets(ModManager manager, CliOptions o, IEnumerable<ModEntry>? pool = null)
    {
        var all = (pool ?? manager.ListInstalledMods(includeDisabled: true)).ToList();

        var file = o.Get("file", "f");
        if (!string.IsNullOrEmpty(file))
        {
            var hit = all.FirstOrDefault(x => string.Equals(x.FileName, file, StringComparison.OrdinalIgnoreCase))
                      ?? all.FirstOrDefault(x => x.FileName.Contains(file, StringComparison.OrdinalIgnoreCase));
            return hit is null ? new List<ModEntry>() : new List<ModEntry> { hit };
        }

        if (o.Has("all", "a")) return all;

        // 位置参数（第一个是子命令，其后按名称模糊匹配）
        var names = o.Positional.Skip(1).ToList();
        // 未给任何选择器时按「全部」处理（remove 有 --yes 兜底，不会误删）
        if (names.Count == 0) return all;

        return names
            .SelectMany(n => all.Where(x =>
                x.FileName.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                (x.ModId ?? "").Contains(n, StringComparison.OrdinalIgnoreCase) ||
                x.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // ---------------- skin ----------------

    private static async Task<int> Skin(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        var username = o.Positional.Count > 0 ? o.Positional[0] : null;
        if (string.IsNullOrEmpty(username))
        {
            Err("用法: chert skin <username>");
            return 1;
        }

        Info($"查询 {username} 的皮肤...");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var skin = await Chert.Core.Skin.SkinFetcher.FetchByUsernameAsync(client, username);
            if (skin is null)
            {
                if (_json) WriteJson(new { found = false, username });
                else Info($"未找到玩家 {username} 的皮肤（可能不是正版用户）");
                return 1;
            }

            if (_json)
            {
                WriteJson(new { found = true, username, skinUrl = skin.SkinUrl, model = skin.Model, capeUrl = skin.CapeUrl });
                return 0;
            }
            Info($"皮肤 URL: {skin.SkinUrl}");
            Info($"模型类型: {skin.Model}");
            if (skin.CapeUrl is not null) Info($"披风 URL: {skin.CapeUrl}");
            return 0;
        }
        catch (Exception ex)
        {
            Err($"查询失败: {ex.Message}");
            return 1;
        }
    }

    // ---------------- dirs ----------------

    private static int Dirs(string[] args)
    {
        CliOptions.Parse(args, ValueKeys);
        var map = new Dictionary<string, string>
        {
            ["gameRoot"] = _gameRoot,
            ["versions"] = PathEx.VersionsDir(_gameRoot),
            ["mods"] = PathEx.ModsDir(_gameRoot),
            ["libraries"] = PathEx.LibrariesDir(_gameRoot),
            ["assets"] = PathEx.AssetsDir(_gameRoot),
            ["resourcePacks"] = PathEx.ResourcePacksDir(_gameRoot),
            ["shaderPacks"] = PathEx.ShaderPacksDir(_gameRoot),
            ["saves"] = PathEx.SavesDir(_gameRoot),
            ["logs"] = LogManager.LogsDir(_gameRoot),
            ["crashReports"] = LogManager.CrashReportsDir(_gameRoot),
            ["profiles"] = Path.Combine(_gameRoot, "mclcs_profiles.json"),
            ["badges"] = Path.Combine(_gameRoot, "mclcs_badges.json")
        };

        if (_json) { WriteJson(map); return 0; }
        foreach (var kv in map) Info($"{kv.Key,-14} {kv.Value}");
        return 0;
    }

    // ---------------- profiles / config ----------------

    private static int Profiles(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        var profile = ProfileStore.Load(_gameRoot);

        var set = o.Get("set");
        if (!string.IsNullOrEmpty(set))
        {
            var eq = set.IndexOf('=');
            if (eq <= 0)
            {
                Err("--set 需要 key=value 形式，例如 --set maxMemoryMb=4096");
                return 1;
            }
            var key = set[..eq].Trim();
            var val = set[(eq + 1)..];

            var prop = typeof(LauncherProfile).GetProperty(key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop is null)
            {
                Err($"未知配置项: {key}");
                return 1;
            }

            try
            {
                var converted = Convert.ChangeType(val, prop.PropertyType);
                prop.SetValue(profile, converted);
                ProfileStore.Save(profile);
                if (_json) WriteJson(new { set = true, key = prop.Name, value = converted?.ToString() });
                else Info($"已设置 {prop.Name} = {converted}");
                return 0;
            }
            catch (Exception ex)
            {
                Err($"设置失败: {ex.Message}");
                return 1;
            }
        }

        if (_json) { WriteJson(profile); return 0; }

        Info($"启动器配置 ({Path.Combine(_gameRoot, "mclcs_profiles.json")})：");
        foreach (var p in typeof(LauncherProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            object? v;
            try { v = p.GetValue(profile); }
            catch { v = "<无法读取>"; }
            Info($"  {p.Name,-28} {v}");
        }
        return 0;
    }

    // ---------------- self update ----------------

    private static async Task<int> SelfUpdate(string[] args)
    {
        CliOptions.Parse(args, ValueKeys);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            var res = await Chert.Core.Update.LauncherUpdater.CheckAsync(GameConstants.LauncherVersion, client);

            if (_json)
            {
                WriteJson(new
                {
                    current = res.CurrentVersion ?? GameConstants.LauncherVersion,
                    latest = res.LatestVersion,
                    available = res.Available,
                    mandatory = res.Mandatory,
                    status = res.Status,
                    changelog = res.Changelog,
                    downloadUrl = res.DownloadUrl,
                    lightDownloadUrl = res.LightDownloadUrl,
                    error = res.Error
                });
                return res.Available ? 0 : 0;
            }

            if (res.Error is not null)
            {
                Err($"检查更新失败: {res.Error}");
                return 1;
            }
            if (!res.Available)
            {
                Info($"当前已是最新（{GameConstants.LauncherVersion}）");
                return 0;
            }
            Info($"有新版本: {res.CurrentVersion} → {res.LatestVersion}{(res.Mandatory ? " [必更]" : "")}");
            if (!string.IsNullOrWhiteSpace(res.Changelog)) Info($"更新日志:\n{res.Changelog}");
            if (!string.IsNullOrWhiteSpace(res.DownloadUrl)) Info($"下载: {res.DownloadUrl}");
            if (!string.IsNullOrWhiteSpace(res.LightDownloadUrl)) Info($"轻量包: {res.LightDownloadUrl}");
            return 0;
        }
        catch (Exception ex)
        {
            Err($"检查更新失败: {ex.Message}");
            return 1;
        }
    }

    // ---------------- logs ----------------

    private static int Logs(string[] args)
    {
        var o = CliOptions.Parse(args, ValueKeys);
        var logs = LogManager.ListLogs(_gameRoot);

        if (logs.Count == 0)
        {
            if (_json) WriteJson(new { count = 0, files = Array.Empty<object>() });
            else Info("暂无日志文件。");
            return 0;
        }

        if (o.Has("list"))
        {
            if (_json)
            {
                WriteJson(new
                {
                    count = logs.Count,
                    files = logs.Select(f => new { name = f.Name, path = f.FullPath, sizeBytes = f.SizeBytes, lastWriteUtc = f.LastWriteUtc, kind = f.Kind })
                });
                return 0;
            }
            foreach (var f in logs)
                Info($"  {f.Kind,-5} {f.Name,-40} {f.SizeBytes / 1024.0,8:F1} KB  {f.LastWriteUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            return 0;
        }

        var pick = o.Get("file", "f");
        var target = string.IsNullOrEmpty(pick)
            ? logs.OrderByDescending(f => f.LastWriteUtc).First()
            : logs.FirstOrDefault(f => f.Name.Contains(pick, StringComparison.OrdinalIgnoreCase))
              ?? throw new IOException($"未找到匹配 {pick} 的日志文件");

        var lines = LogManager.ParseLines(LogManager.ReadLog(target.FullPath));
        if (o.Has("errors")) lines = LogManager.Filter(lines, o.Get("keyword", "k"), onlyErrors: true);
        else if (o.Get("keyword", "k") is { } kw) lines = LogManager.Filter(lines, kw);

        var take = int.TryParse(o.Get("lines", "n"), out var n) ? n : 40;
        var tail = lines.Skip(Math.Max(0, lines.Count - take)).ToList();

        if (_json)
        {
            WriteJson(new
            {
                file = target.Name,
                path = target.FullPath,
                total = lines.Count,
                shown = tail.Count,
                lines = tail.Select(l => new { index = l.Index, severity = l.Severity.ToString(), text = l.Text })
            });
            return 0;
        }

        Info($"日志 {target.Name}（{lines.Count} 行，显示末尾 {tail.Count} 行）：");
        foreach (var l in tail) Info($"  {l.Index,6} [{l.Severity}] {l.Text}");
        return 0;
    }

    // ---------------- completion ----------------

    private static int Completion(string[] args)
    {
        var shell = args.Length > 0 ? args[0].ToLowerInvariant() : "bash";
        var commands = "launch list versions install modpack mods skin dirs profiles config update self-update logs completion version help";
        var modSubs = "list check updates verify update enable disable remove";

        switch (shell)
        {
            case "bash":
                Console.WriteLine($"# chert bash 补全 —— 保存为 /etc/bash_completion.d/chert 或 source 之");
                Console.WriteLine("_chert() {");
                Console.WriteLine("  local cur prev");
                Console.WriteLine("  cur=\"${COMP_WORDS[COMP_CWORD]}\"");
                Console.WriteLine("  prev=\"${COMP_WORDS[COMP_CWORD-1]}\"");
                Console.WriteLine($"  local cmds=\"{commands}\"");
                Console.WriteLine($"  local modsubs=\"{modSubs}\"");
                Console.WriteLine("  if [[ \"$prev\" == \"mods\" ]]; then COMPREPLY=( $(compgen -W \"$modsubs\" -- \"$cur\") ); return 0; fi");
                Console.WriteLine("  COMPREPLY=( $(compgen -W \"$cmds\" -- \"$cur\") )");
                Console.WriteLine("  return 0");
                Console.WriteLine("}");
                Console.WriteLine("complete -F _chert chert");
                return 0;

            case "powershell" or "pwsh":
                Console.WriteLine("# chert PowerShell 补全 —— 追加到 $PROFILE");
                Console.WriteLine("$chertCmds = @(" + string.Join(", ", commands.Split(' ').Select(c => $"'{c}'")) + ")");
                Console.WriteLine("$chertModSubs = @(" + string.Join(", ", modSubs.Split(' ').Select(c => $"'{c}'")) + ")");
                Console.WriteLine("Register-ArgumentCompleter -Native -CommandName chert -ScriptBlock {");
                Console.WriteLine("    param($wordToComplete, $ast, $cursorPos)");
                Console.WriteLine("    $prev = $ast.CommandElements[-2].Value");
                Console.WriteLine("    if ($prev -eq 'mods') { $chertModSubs | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object { [System.Management.Automation.CompletionResult]::new($_) } }");
                Console.WriteLine("    else { $chertCmds | Where-Object { $_ -like \"$wordToComplete*\" } | ForEach-Object { [System.Management.Automation.CompletionResult]::new($_) } }");
                Console.WriteLine("}");
                return 0;

            case "zsh":
                Console.WriteLine("#compdef chert");
                Console.WriteLine($"_chert() {{ local -a cmds; cmds=({commands}); _describe 'command' cmds }}");
                Console.WriteLine("_chert \"$@\"");
                return 0;

            default:
                Err("用法: chert completion <bash|powershell|zsh>");
                return 1;
        }
    }

    // ---------------- version / help ----------------

    private static int Version(string[] args)
    {
        if (_json)
        {
            WriteJson(new
            {
                name = GameConstants.LauncherName,
                displayName = GameConstants.LauncherDisplayName,
                version = GameConstants.LauncherVersion,
                platform = GameConstants.PlatformId
            });
            return 0;
        }
        Console.WriteLine($"{GameConstants.LauncherDisplayName} {GameConstants.LauncherVersion} ({GameConstants.LauncherName})");
        return 0;
    }

    private static void PrintHelp(string? command)
    {
        if (command is not null)
        {
            switch (command)
            {
                case "mods":
                    Console.WriteLine("chert mods — Mod 管理");
                    Console.WriteLine();
                    Console.WriteLine("  chert mods list                     列出已安装 Mod（含禁用项）");
                    Console.WriteLine("  chert mods check                    依赖 / 冲突检查");
                    Console.WriteLine("  chert mods updates                  在线检查新版本");
                    Console.WriteLine("  chert mods verify   [--all|--file x] 核验完整性 / 元数据 / SHA-1");
                    Console.WriteLine("  chert mods update   [--all|--file x] 一键更新");
                    Console.WriteLine("  chert mods enable   [--all|--file x] 批量启用");
                    Console.WriteLine("  chert mods disable  [--all|--file x] 批量禁用");
                    Console.WriteLine("  chert mods remove   --file x [--yes] 卸载");
                    return;
                case "logs":
                    Console.WriteLine("chert logs — 日志查看");
                    Console.WriteLine();
                    Console.WriteLine("  chert logs --list                   列出日志文件");
                    Console.WriteLine("  chert logs [--file <名>] [--lines N] [--errors] [--keyword k]");
                    return;
                case "profiles" or "config":
                    Console.WriteLine("chert profiles — 启动器配置");
                    Console.WriteLine();
                    Console.WriteLine("  chert profiles                      列出所有配置项");
                    Console.WriteLine("  chert profiles --set key=value      修改配置项");
                    return;
            }
        }

        Console.WriteLine($"{GameConstants.LauncherDisplayName} v{GameConstants.LauncherVersion} — Minecraft 启动器");
        Console.WriteLine();
        Console.WriteLine("用法: chert <命令> [选项]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  launch     <versionId> [--username <name>] [--memory <MB>] [--java <path>]");
        Console.WriteLine("  list       [--game-dir <path>]                 列出已安装版本（别名 versions）");
        Console.WriteLine("  install    <vanilla|fabric|forge> <versionId>");
        Console.WriteLine("  modpack    <modrinth|curseforge|auto> <file>");
        Console.WriteLine("  mods       <list|check|updates|verify|update|enable|disable|remove>");
        Console.WriteLine("  skin       <username>");
        Console.WriteLine("  dirs                                           输出关键目录路径");
        Console.WriteLine("  profiles   [--set key=value]                   查看 / 修改启动器配置");
        Console.WriteLine("  update                                         检查启动器自身更新");
        Console.WriteLine("  logs       [--list] [--file <名>] [--lines N]   查看日志");
        Console.WriteLine("  completion <bash|powershell|zsh>                输出 Shell 补全脚本");
        Console.WriteLine("  version");
        Console.WriteLine("  help       [命令]");
        Console.WriteLine();
        Console.WriteLine("全局选项:");
        Console.WriteLine("  --game-dir <path>      指定游戏目录（默认见下）");
        Console.WriteLine("  --curseforge-key <key> CurseForge API Key（覆盖用户设置与内置 Key）");
        Console.WriteLine("  --curseforge-root <url> CurseForge API Root（可指向镜像，默认官方）");
        Console.WriteLine("  --json                 以 JSON 输出，便于脚本解析");
        Console.WriteLine("  --quiet / -q           静默模式（--json 时自动生效）");
        Console.WriteLine();
        Console.WriteLine("退出码: 0=成功  1=用法或异常  2=部分失败（核验 / 更新 / 启用 / 卸载）");
        Console.WriteLine();
        Console.WriteLine($"默认游戏目录: {GameConstants.DefaultGameRoot}");
    }

    // ---------------- 参数解析 ----------------

    private static readonly HashSet<string> ValueKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "username", "user", "u",
        "memory", "m",
        "java",
        "game-dir", "gamedir", "g",
        "file", "f",
        "set",
        "lines", "n",
        "keyword", "k"
    };

    /// <summary>简易参数解析器：支持 --key value、--key=value、布尔开关与位置参数。</summary>
    private sealed class CliOptions
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Positional { get; } = new();

        public static CliOptions Parse(string[] args, HashSet<string> valueKeys)
        {
            var o = new CliOptions();
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith('-')) { o.Positional.Add(a); continue; }

                var eq = a.IndexOf('=');
                if (eq > 0)
                {
                    o._values[Normalize(a[..eq])] = a[(eq + 1)..];
                    continue;
                }

                var key = Normalize(a);
                if (valueKeys.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    o._values[key] = args[++i];
                else
                    o._flags.Add(key);
            }
            return o;
        }

        private static string Normalize(string s) => s.TrimStart('-').ToLowerInvariant();

        public bool Has(params string[] names) => names.Any(n => _flags.Contains(Normalize(n)));

        public string? Get(params string[] names)
        {
            foreach (var n in names)
                if (_values.TryGetValue(Normalize(n), out var v)) return v;
            return null;
        }
    }

    private class CliLogger : Chert.Core.Download.ILogger
    {
        public void Log(string msg)
        {
            if (!_quiet && !_json) Console.WriteLine($"[Chert] {msg}");
        }
    }
}
