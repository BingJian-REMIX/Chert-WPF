using Chert.App.Services;
using Chert.Core.Utils;

namespace mclcs;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var cmd = args[0].ToLowerInvariant();
        var remaining = args[1..];

        return cmd switch
        {
            "launch" => await Launch(remaining),
            "list" => await ListVersions(remaining),
            "install" => await Install(remaining),
            "mods" => await Mods(remaining),
            "modpack" => await Modpack(remaining),
            "skin" => await Skin(remaining),
            "version" => Version(),
            "help" or "--help" or "-h" => PrintHelpReturn(),
            _ => UnknownCommand(cmd)
        };
    }

    private static int PrintHelpReturn()
    {
        PrintHelp();
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"未知命令: {cmd}。运行 mclcs help 查看帮助。");
        return 1;
    }

    private static async Task<int> Launch(string[] args)
    {
        var gameRoot = GameConstants.DefaultGameRoot;
        string? versionId = null;
        string? username = null;
        int? memory = null;
        string? java = null;

        var i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--game-dir" when i + 1 < args.Length: gameRoot = args[++i]; break;
                case "--username" when i + 1 < args.Length: username = args[++i]; break;
                case "--memory" or "-m" when i + 1 < args.Length && int.TryParse(args[i + 1], out var m): memory = m; i++; break;
                case "--java" when i + 1 < args.Length: java = args[++i]; break;
                default:
                    if (!args[i].StartsWith("-")) versionId = args[i];
                    break;
            }
            i++;
        }

        if (string.IsNullOrEmpty(versionId))
        {
            Console.Error.WriteLine("用法: mclcs launch <versionId> [--username name] [--memory MB] [--java path] [--game-dir path]");
            return 1;
        }

        var svc = new LauncherService(gameRoot);
        svc.Logged += msg => Console.WriteLine($"[MCLCS] {msg}");

        Console.WriteLine($"启动版本: {versionId}");
        try
        {
            var overrides = new LaunchCliOverrides { Username = username, MaxMemoryMb = memory, JavaPath = java };
            var result = await svc.LaunchAsync(versionId, cliOverrides: overrides);
            if (result.CrashReportPath is not null)
                Console.WriteLine($"检测到崩溃报告: {result.CrashReportPath} (退出码 {result.ExitCode})");
            else
                Console.WriteLine($"游戏正常退出 (退出码 {result.ExitCode})");
            return result.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动失败: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ListVersions(string[] args)
    {
        var gameRoot = GameConstants.DefaultGameRoot;
        if (args.Length >= 2 && args[0] == "--game-dir") gameRoot = args[1];

        var svc = new LauncherService(gameRoot);
        var versions = svc.ListInstalledVersions();

        if (versions.Count == 0)
        {
            Console.WriteLine("暂无已安装版本。使用 mclcs install <vanilla|fabric|forge> <version> 安装。");
            return 0;
        }

        Console.WriteLine($"已安装版本 ({gameRoot})：");
        foreach (var (id, type) in versions)
            Console.WriteLine($"  {id,-30} {type}");
        return 0;
    }

    private static async Task<int> Install(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: mclcs install <vanilla|fabric|forge> <versionId> [--game-dir path]");
            return 1;
        }

        var installType = args[0].ToLowerInvariant();
        var versionId = args[1];
        var gameRoot = GameConstants.DefaultGameRoot;
        if (args.Length >= 4 && args[2] == "--game-dir") gameRoot = args[3];

        if (installType is not ("vanilla" or "fabric" or "forge"))
        {
            Console.Error.WriteLine("类型必须是 vanilla、fabric 或 forge");
            return 1;
        }

        var svc = new LauncherService(gameRoot);
        svc.Logged += msg => Console.WriteLine($"[MCLCS] {msg}");

        Console.WriteLine($"安装 {installType} {versionId} → {gameRoot}");
        try
        {
            await svc.InstallAsync(installType, versionId);
            Console.WriteLine("安装完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"安装失败: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> Modpack(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("用法: mclcs modpack <modrinth> <文件路径> [--game-dir path]");
            return 1;
        }

        var packType = args[0].ToLowerInvariant();
        var filePath = args[1];
        var gameRoot = GameConstants.DefaultGameRoot;
        if (args.Length >= 4 && args[2] == "--game-dir") gameRoot = args[3];

        if (packType != "modrinth")
        {
            Console.Error.WriteLine("整合包类型当前仅支持 modrinth");
            return 1;
        }

        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"文件不存在: {filePath}");
            return 1;
        }

        Console.WriteLine($"安装 {packType} 整合包: {filePath} → {gameRoot}");
        try
        {
            if (packType == "modrinth")
            {
                var installer = new Chert.Core.Installers.ModpackInstaller(
                    gameRoot, new HttpClient(), new Chert.Core.Download.HttpDownloader(new HttpClient()),
                    new CliLogger());
                await installer.InstallAsync(filePath);
            }
            else
            {
                Console.Error.WriteLine("整合包类型当前仅支持 Modrinth .mrpack。");
                return 1;
            }
            Console.WriteLine("整合包安装完成。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"安装失败: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> Mods(string[] args)
    {
        var gameRoot = GameConstants.DefaultGameRoot;
        var subCmd = args.Length > 0 && !args[0].StartsWith("-") ? args[0].ToLowerInvariant() : "list";

        // 解析 --game-dir
        var i = args.Length > 0 && args[0].StartsWith("-") ? 0 : 1;
        while (i < args.Length)
        {
            if (args[i] == "--game-dir" && i + 1 < args.Length) { gameRoot = args[i + 1]; break; }
            i++;
        }

        switch (subCmd)
        {
            case "list":
            {
                var manager = new Chert.Core.Mods.ModManager(gameRoot, new HttpClient(), new Chert.Core.Download.HttpDownloader(new HttpClient()));
                var mods = manager.ListInstalledMods();
                Console.WriteLine($"已安装 Mod ({mods.Count})：");
                foreach (var m in mods)
                    Console.WriteLine($"  {m.Name,-40} v{m.InstalledVersion,-12} [{m.Loader}]");
                return 0;
            }
            case "check":
            {
                var manager = new Chert.Core.Mods.ModManager(gameRoot, new HttpClient(), new Chert.Core.Download.HttpDownloader(new HttpClient()));
                var results = manager.CheckDependencies();
                if (results.Count == 0)
                {
                    Console.WriteLine("所有依赖已满足。");
                    return 0;
                }
                Console.WriteLine($"依赖问题 ({results.Count})：");
                foreach (var r in results)
                {
                    Console.WriteLine($"  {r.ModName} ({r.ModId} v{r.ModVersion}):");
                    foreach (var dep in r.Missing)
                        Console.WriteLine($"    缺失: {dep.DependencyId} ({dep.VersionRange}) [必需]");
                    foreach (var c in r.Conflicts)
                        Console.WriteLine($"    冲突: {c.ConflictId} (已安装 {c.InstalledVersion}, 冲突 {c.ConflictRange})");
                }
                return 0;
            }
            case "updates":
            {
                var manager = new Chert.Core.Mods.ModManager(gameRoot, new HttpClient(), new Chert.Core.Download.HttpDownloader(new HttpClient()));
                Console.WriteLine("检查更新中...");
                var mods = await manager.CheckForUpdatesAsync();
                var hasUpdate = mods.Where(m => m.HasUpdate).ToList();
                Console.WriteLine($"检查完成。{hasUpdate.Count}/{mods.Count} 个 Mod 有新版本：");
                foreach (var m in hasUpdate)
                    Console.WriteLine($"  {m.Name}: {m.InstalledVersion} → {m.LatestVersion}  {m.ProjectUrl}");
                return 0;
            }
            default:
                Console.Error.WriteLine("用法: mclcs mods <list|check|updates> [--game-dir path]");
                return 1;
        }
    }

    private static async Task<int> Skin(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("用法: mclcs skin <username>");
            return 1;
        }

        var username = args[0];
        Console.WriteLine($"查询 {username} 的皮肤...");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var skin = await Chert.Core.Skin.SkinFetcher.FetchByUsernameAsync(client, username);
            if (skin is not null)
            {
                Console.WriteLine($"皮肤 URL: {skin.SkinUrl}");
                Console.WriteLine($"模型类型: {skin.Model}");
                if (skin.CapeUrl is not null)
                    Console.WriteLine($"披风 URL: {skin.CapeUrl}");
            }
            else
            {
                Console.WriteLine($"未找到玩家 {username} 的皮肤（可能不是正版用户）");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"查询失败: {ex.Message}");
            return 1;
        }
    }

    private static int Version()
    {
        Console.WriteLine($"MCLCS {GameConstants.LauncherVersion}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"{GameConstants.LauncherDisplayName} v{GameConstants.LauncherVersion} — Minecraft 启动器");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  launch  <versionId> [--username <name>] [--memory <MB>] [--java <path>] [--game-dir <path>]");
        Console.WriteLine("  list    [--game-dir <path>]");
        Console.WriteLine("  install <vanilla|fabric|forge> <versionId> [--game-dir <path>]");
        Console.WriteLine("  modpack <modrinth> <file> [--game-dir <path>]");
        Console.WriteLine("  mods    <list|check|updates> [--game-dir <path>]");
        Console.WriteLine("  skin    <username>");
        Console.WriteLine("  version");
        Console.WriteLine("  help");
        Console.WriteLine();
        Console.WriteLine($"默认游戏目录: {GameConstants.DefaultGameRoot}");
    }

    private class CliLogger : Chert.Core.Download.ILogger
    {
        public void Log(string msg) => Console.WriteLine($"[MCLCS] {msg}");
    }
}
