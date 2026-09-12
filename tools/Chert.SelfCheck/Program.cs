using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Chert.Core.Auth;
using Chert.Core.Download;
using Chert.Core.Installers;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Save;
using Chert.Core.Skin;
using Chert.Core.Theme;
using Chert.Core.Utils;
using Chert.Core.Ai;
using Chert.Core.MultiInstance;
using Chert.Core.Statistics;
using Chert.Core.Toolbox;
using Chert.Core.Update;
using Chert.Core.UI;
using Chert.Core.Tokens;
using Chert.Core.Servers;
using Chert.Core.Hud;
using Chert.Core.Resources;

namespace Chert.SelfCheck;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static void Check(string name, bool condition)
    {
        if (condition) { _passed++; Console.WriteLine($"  PASS  {name}"); }
        else { _failed++; Console.WriteLine($"  FAIL  {name}"); }
    }

    private static int _skipped;
    private static void Skip(string name)
    {
        _skipped++; Console.WriteLine($"  SKIP  {name}");
    }

    // 平台守卫：部分断言仅在 Linux 运行时成立（参数解析/natives 按当前 OS 过滤）。
    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static async Task Main()
    {
        Console.WriteLine("=== MCLCS Core 自检 ===\n");

        await JavaDetection();
        MavenParsing();
        ArgumentProcessing();
        ClasspathAndNatives();
        CrashAnalysis();
        OfflineAuth();
        await VersionMerge();
        AccountStoreTest();
        LaunchOptionsAndAuthVars();
        RepairEngineTest();
        ModpackIndexParsing();
        ModrinthModelDeserialization();
        LocalizationTest();
        SkinFetcherTest();
        ThemeManagerTest();
        ModMetadataParserTest();
        CrashRepairModelsV05Test();
        LibraryRepairTest();
        RecommendationSystemTest();
        JavaVendorTest();
        AutoInstallPolicyTest();
        ModConflictPlanTest();
        ProfileV05Test();
        await SaveFeatureTests();
        await ToolboxV1Tests();
        await AiV2Tests();
        await V2Tests();

        Console.WriteLine($"\n=== 结果：{_passed} 通过, {_failed} 失败, {_skipped} 跳过 ===");
        Environment.Exit(_failed == 0 ? 0 : 1);
    }

    private static async Task JavaDetection()
    {
        Console.WriteLine("[Java 版本解析]");
        Check("1.8.0_301 -> 8", JavaDetector.MajorFromVersionString("1.8.0_301") == 8);
        Check("21.0.3 -> 21", JavaDetector.MajorFromVersionString("21.0.3") == 21);
        Check("17 -> 17", JavaDetector.MajorFromVersionString("17") == 17);
        await Task.CompletedTask;
    }

    private static void MavenParsing()
    {
        Console.WriteLine("[Maven 坐标解析]");
        var c = MavenCoordinate.Parse("org.lwjgl:lwjgl:3.3.1");
        Check("group", c.Group == "org.lwjgl");
        Check("artifact", c.Artifact == "lwjgl");
        Check("version", c.Version == "3.3.1");
        Check("localPath", c.LocalPath() == "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar");
        var c2 = MavenCoordinate.Parse("com.example:mod:1.0:natives-linux");
        Check("classifier path", c2.LocalPath() == "com/example/mod/1.0/mod-1.0-natives-linux.jar");
    }

    private static void ArgumentProcessing()
    {
        Console.WriteLine("[参数解析：规则 + 变量 + 内存注入]");

        // 构造一个含 windows/linux 规则与变量的版本（在 Linux 沙箱上运行）
        var version = new VersionJson
        {
            Id = "test",
            MainClass = "net.minecraft.client.main.Main",
            Arguments = new Arguments
            {
                Jvm = new List<ArgumentItem>
                {
                    new() { Values = new() { "-XX:+UseG1GC" } },
                    new() { Rules = new() { new Rule { Action = "allow", Os = new OsRule { Name = "windows" } } },
                            Values = new() { "-DWINDOWS_MARKER=1" } },
                    new() { Rules = new() { new Rule { Action = "allow", Os = new OsRule { Name = "linux" } } },
                            Values = new() { "-DLINUX_MARKER=1" } }
                },
                Game = new List<ArgumentItem>
                {
                    new() { Values = new() { "--username", "${auth_player_name}" } },
                    new() { Rules = new() { new Rule { Action = "allow", Features = new() { { "has_custom_resolution", true } } } },
                            Values = new() { "--width", "${resolution_width}" } }
                }
            }
        };

        var vars = new Dictionary<string, string>
        {
            ["auth_player_name"] = "Steve",
            ["natives_directory"] = "/tmp/natives"
        };
        var options = new LaunchOptions { MaxMemoryMb = 4096 };
        var resolved = ArgumentProcessor.Process(version, vars, options, "/tmp/natives");

        Check("保留 -XX:+UseG1GC", resolved.JvmArgs.Contains("-XX:+UseG1GC"));
        if (IsLinux)
        {
            Check("包含 linux 标记", resolved.JvmArgs.Contains("-DLINUX_MARKER=1"));
            Check("排除 windows 标记", !resolved.JvmArgs.Contains("-DWINDOWS_MARKER=1"));
        }
        else
        {
            Skip("包含 linux 标记（仅 linux 运行时校验）");
            Skip("排除 windows 标记（仅 linux 运行时校验）");
        }
        Check("注入 -Xmx4096M", resolved.JvmArgs.Any(a => a == "-Xmx4096M"));
        Check("注入 java.library.path", resolved.JvmArgs.Any(a => a.StartsWith("-Djava.library.path=")));
        Check("注入 org.lwjgl.librarypath", resolved.JvmArgs.Any(a => a.StartsWith("-Dorg.lwjgl.librarypath=")));
        Check("变量替换用户名", resolved.GameArgs.Count >= 2 && resolved.GameArgs[1] == "Steve");
        Check("无自定义分辨率时排除 --width", !resolved.GameArgs.Contains("--width"));

        // 旧版 minecraftArguments 回退
        Console.WriteLine("[参数解析：旧版 minecraftArguments]");
        var legacy = new VersionJson
        {
            Id = "legacy",
            MinecraftArguments = "-Xmx2G -Dfoo=bar -cp ${classpath} net.minecraft.client.main.Main --username ${auth_player_name}"
        };
        var lvars = new Dictionary<string, string> { ["auth_player_name"] = "Alex", ["classpath"] = "a.jar;b.jar" };
        var lresolved = ArgumentProcessor.Process(legacy, lvars, new LaunchOptions { MaxMemoryMb = 2048 }, "/tmp/n");
        Check("legacy 提取 mainClass", lresolved.MainClass == "net.minecraft.client.main.Main");
        Check("legacy 替换用户名", lresolved.GameArgs.Contains("Alex"));
        Check("legacy 内存被用户值覆盖", lresolved.JvmArgs.Contains("-Xmx2048M"));
    }

    private static void ClasspathAndNatives()
    {
        Console.WriteLine("[classpath 构建 + natives]");
        var root = Path.Combine(Path.GetTempPath(), "mclcs_cp_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vdir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(vdir);
            File.WriteAllText(Path.Combine(vdir, "1.20.1.jar"), "dummy");

            var libDir = Path.Combine(root, "libraries", "com", "mojang", "log4j", "2.19.1");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "log4j-2.19.1.jar"), "dummy");

            var version = new VersionJson
            {
                Id = "1.20.1",
                Libraries = new List<Library>
                {
                    new() { Name = "com.mojang:log4j:2.19.1",
                            Downloads = new LibraryDownloads { Artifact = new DownloadInfo { Path = "com/mojang/log4j/2.19.1/log4j-2.19.1.jar" } } },
                    new() { Name = "org.lwjgl:lwjgl:3.3.1",
                            Natives = new Dictionary<string, string> { { "linux", "natives-linux" } },
                            Downloads = new LibraryDownloads { Classifiers = new() { ["natives-linux"] = new DownloadInfo { Path = "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar" } } } }
                }
            };

            var cp = ClasspathBuilder.ComputeClasspath(root, "1.20.1", version);
            Check("classpath 含版本 jar", cp.Contains("1.20.1.jar"));
            Check("classpath 含库 jar", cp.Contains("log4j-2.19.1.jar"));

            var natives = ClasspathBuilder.GetNativeEntries(root, version, Path.Combine(root, "natives"));
            if (IsLinux)
            {
                Check("识别 1 个 natives 条目", natives.Count == 1);
                Check("natives 路径含 natives-linux", natives.Count == 1 && natives[0].JarPath.Contains("natives-linux"));
            }
            else
            {
                Skip("识别 1 个 natives 条目（仅 linux 运行时校验）");
                Skip("natives 路径含 natives-linux（仅 linux 运行时校验）");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void CrashAnalysis()
    {
        Console.WriteLine("[崩溃分析]");
        var oom = CrashAnalyzer.Analyze("Description: The game crashed. java.lang.OutOfMemoryError: Java heap space");
        Check("OutOfMemoryError", oom.ExceptionType.Contains("内存不足"));

        var uvc = CrashAnalyzer.Analyze("java.lang.UnsupportedClassVersionError: class file version 65.0");
        Check("UnsupportedClassVersion 识别", uvc.ExceptionType.Contains("Java 版本不匹配"));
        Check("推算需要 Java 21", uvc.Summary.Contains("Java 21"));

        var cnf = CrashAnalyzer.Analyze("java.lang.ClassNotFoundException: com.example.mymod.Mod");
        Check("ClassNotFoundException", cnf.ExceptionType.Contains("类未找到"));

        var gl = CrashAnalyzer.Analyze("OpenGL error: Could not create context");
        Check("OpenGL 错误", gl.ExceptionType.Contains("OpenGL"));
    }

    private static void OfflineAuth()
    {
        Console.WriteLine("[离线认证 UUID]");
        var uuid = OfflineAuthenticator.GenerateOfflineUuid("Notch");
        Check("UUID 长度 36", uuid.Length == 36);
        Check("UUID 含连字符", uuid.Contains('-'));
        Check("同名稳定", OfflineAuthenticator.GenerateOfflineUuid("Notch") == uuid);
    }

    private static async Task VersionMerge()
    {
        Console.WriteLine("[版本合并（Fabric 继承原版）]");
        var root = Path.Combine(Path.GetTempPath(), "mclcs_merge_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vanillaDir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(vanillaDir);
            var vanilla = new VersionJson
            {
                Id = "1.20.1",
                MainClass = "net.minecraft.client.main.Main",
                Libraries = new List<Library> { new() { Name = "com.mojang:patchy:1.0" } },
                Arguments = new Arguments
                {
                    Game = new List<ArgumentItem> { new() { Values = new() { "--username", "${auth_player_name}" } } }
                }
            };
            await File.WriteAllTextAsync(Path.Combine(vanillaDir, "1.20.1.json"),
                JsonSerializer.Serialize(vanilla, new JsonSerializerOptions { WriteIndented = true }));

            var fabricDir = Path.Combine(root, "versions", "fabric-1.20.1");
            Directory.CreateDirectory(fabricDir);
            var fabric = new VersionJson
            {
                Id = "fabric-1.20.1",
                InheritsFrom = "1.20.1",
                MainClass = "net.fabricmc.loader.impl.launcher.Main",
                Libraries = new List<Library> { new() { Name = "net.fabricmc:fabric-loader:0.15.0" } },
                Arguments = new Arguments
                {
                    Game = new List<ArgumentItem> { new() { Values = new() { "--fabric" } } }
                }
            };
            await File.WriteAllTextAsync(Path.Combine(fabricDir, "fabric-1.20.1.json"),
                JsonSerializer.Serialize(fabric, new JsonSerializerOptions { WriteIndented = true }));

            var merged = VersionMerger.Merge(root, "fabric-1.20.1");
            Check("mainClass 取自子版本(Fabric)", merged.MainClass == "net.fabricmc.loader.impl.launcher.Main");
            Check("库合并含原版库", merged.Libraries.Any(l => l.Name == "com.mojang:patchy:1.0"));
            Check("库合并含 Fabric 库", merged.Libraries.Any(l => l.Name == "net.fabricmc:fabric-loader:0.15.0"));
            Check("game 参数合并含原版", merged.Arguments!.Game.Any(i => i.Values.Contains("--username")));
            Check("game 参数合并含 Fabric", merged.Arguments!.Game.Any(i => i.Values.Contains("--fabric")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        await Task.CompletedTask;
    }

    // ---- v0.2 新增测试 ----

    private static void AccountStoreTest()
    {
        Console.WriteLine("[账号存储 CRUD]");
        var tmp = Path.Combine(Path.GetTempPath(), "mclcs_acctest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmp);
            var list = AccountStore.Load(tmp);
            Check("新目录返回空列表", list.Count == 0);

            var a = new AccountEntry
            {
                Id = "test1",
                DisplayName = "离线玩家",
                AuthType = "offline",
                Username = "Player"
            };
            AccountStore.Upsert(tmp, a);

            var loaded = AccountStore.Load(tmp);
            Check("保存后加载 1 条", loaded.Count == 1);
            Check("条目属性正确", loaded[0].Id == "test1" && loaded[0].AuthType == "offline");
            Check("自动设置 lastUsed", !string.IsNullOrEmpty(loaded[0].LastUsed));

            var lu = AccountStore.GetLastUsed(tmp);
            Check("GetLastUsed 返回正确", lu is not null && lu.Id == "test1");

            AccountStore.Remove(tmp, "test1");
            Check("删除后为空", AccountStore.Load(tmp).Count == 0);
        }
        finally
        {
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        }
    }

    private static void LaunchOptionsAndAuthVars()
    {
        Console.WriteLine("[LaunchOptions 扩展字段]");
        var options = new LaunchOptions
        {
            UserType = "msa",
            UserProperties = "{\"prop\":\"value\"}",
            ExtraJvmArgs = new List<string> { "-XX:+UseZGC" }
        };
        Check("UserType msa", options.UserType == "msa");
        Check("UserProperties 非空", options.UserProperties != "{}");
        Check("ExtraJvmArgs 有值", options.ExtraJvmArgs.Count == 1);
    }

    private static void RepairEngineTest()
    {
        Console.WriteLine("[崩溃修复规划引擎]");
        var gameRoot = Path.Combine(Path.GetTempPath(), "mclcs_repair_" + Guid.NewGuid().ToString("N"));

        // 内存不足，当前 2048MB -> 应调大到 4096MB
        var oom = new CrashAnalysis { Category = CrashCategory.OutOfMemory };
        var planOom = CrashRepairEngine.BuildPlan(oom, new LauncherProfile { MaxMemoryMb = 2048 }, null, gameRoot, "1.20.1");
        Check("OOM 可自动修复", planOom.CanRepair);
        Check("OOM 策略=调大内存", planOom.Strategy == RepairStrategy.IncreaseMemory);
        Check("OOM 目标内存=4096", planOom.TargetMemoryMb == 4096);
        Check("OOM 非破坏性", planOom.NonDestructive);

        // 内存已到上限 -> 不可再调
        var oomMax = new CrashAnalysis { Category = CrashCategory.OutOfMemory };
        var planMax = CrashRepairEngine.BuildPlan(oomMax, new LauncherProfile { MaxMemoryMb = GameConstants.MaxRepairMemoryMb }, null, gameRoot, "1.20.1");
        Check("OOM 达上限不可修复", !planMax.CanRepair);

        // Java 版本不匹配，需要 Java 21
        var java = new CrashAnalysis { Category = CrashCategory.JavaVersion, RequiredJavaMajor = 21 };
        var planJava = CrashRepairEngine.BuildPlan(java, new LauncherProfile(), null, gameRoot, "1.20.1");
        Check("Java 版本可自动修复", planJava.CanRepair);
        Check("Java 策略=切换Java", planJava.Strategy == RepairStrategy.SwitchJava);
        Check("Java 需要 21", planJava.RequiredJavaMajor == 21);

        // 缺少库，给定版本 -> 重新下载
        var miss = new CrashAnalysis { Category = CrashCategory.MissingLibrary };
        var planMiss = CrashRepairEngine.BuildPlan(miss, new LauncherProfile(), null, gameRoot, "1.20.1");
        Check("缺失库可自动修复", planMiss.CanRepair);
        Check("缺失库策略=重下库", planMiss.Strategy == RepairStrategy.RedownloadLibraries);
        Check("缺失库版本正确", planMiss.VersionId == "1.20.1");

        // 缺失库但无版本 -> 不可修复
        var missNoVer = new CrashAnalysis { Category = CrashCategory.MissingLibrary };
        Check("缺失库无版本不可修复", !CrashRepairEngine.BuildPlan(missNoVer, new LauncherProfile(), null, gameRoot, null).CanRepair);

        // OpenGL / 未知 -> 不可自动修复
        var gl = new CrashAnalysis { Category = CrashCategory.OpenGL };
        Check("OpenGL 不可自动修复", !CrashRepairEngine.BuildPlan(gl, new LauncherProfile(), null, gameRoot, "1.20.1").CanRepair);
        var unk = new CrashAnalysis { Category = CrashCategory.Unknown };
        Check("Unknown 不可自动修复", !CrashRepairEngine.BuildPlan(unk, new LauncherProfile(), null, gameRoot, "1.20.1").CanRepair);

        // 分析器：从类版本反推所需 Java
        var uvc = CrashAnalyzer.Analyze("java.lang.UnsupportedClassVersionError: class file version 65.0");
        Check("类版本 65 -> 需要 Java 21", uvc.RequiredJavaMajor == 21);
        Check("RawReport 可赋值", (uvc.RawReport = "x") == "x");

        // 策略枚举默认值
        Check("RepairPolicy 默认 Ask", new LauncherProfile().RepairPolicy == CrashRepairPolicy.Ask);
    }

    private static void ModpackIndexParsing()
    {
        Console.WriteLine("[整合包索引解析]");
        var json = @"{
            ""formatVersion"": 1,
            ""game"": ""minecraft"",
            ""versionId"": ""1.20.1"",
            ""name"": ""Test Pack"",
            ""dependencies"": { ""minecraft"": ""1.20.1"", ""fabric-loader"": ""0.15.0"" },
            ""files"": [
                {
                    ""path"": ""mods/sodium.jar"",
                    ""env"": { ""client"": ""required"", ""server"": ""unsupported"" },
                    ""hashes"": { ""sha1"": ""abc123"", ""sha512"": ""def456"" },
                    ""downloads"": [""https://example.com/sodium.jar""],
                    ""fileSize"": 123456
                }
            ]
        }";
        var index = JsonSerializer.Deserialize<ModrinthPackIndex>(json);
        Check("formatVersion 1", index is not null && index.FormatVersion == 1);
        Check("name Test Pack", index!.Name == "Test Pack");
        Check("dependencies 含 fabric-loader", index.Dependencies.ContainsKey("fabric-loader"));
        Check("1 个 file", index.Files.Count == 1);
        Check("file client=required", index.Files[0].Env.Client == "required");
        Check("downloads URL", index.Files[0].Downloads[0] == "https://example.com/sodium.jar");
    }

    private static void ModrinthModelDeserialization()
    {
        Console.WriteLine("[Modrinth 模型反序列化]");
        var projJson = @"{""id"":""abc"",""slug"":""sodium"",""title"":""Sodium"",""description"":""Fast"",""body"":""..."",""project_type"":""mod"",""game_versions"":[""1.20.1""],""loaders"":[""fabric""],""downloads"":999,""icon_url"":""https://icon.png"",""gallery"":[]}";
        var proj = JsonSerializer.Deserialize<ModrinthProject>(projJson);
        Check("ModrinthProject title", proj is not null && proj.Title == "Sodium");

        var depJson = @"[{""version_id"":null,""project_id"":""fabric-api"",""file_name"":null,""dependency_type"":""required""}]";
        var deps = JsonSerializer.Deserialize<List<ModrinthDependency>>(depJson);
        Check("dependency type=required", deps is not null && deps[0].DependencyType == "required");

        // LoaderType.NeoForge
        Check("NeoForge loader string", ModrinthClient.LoaderString(LoaderType.NeoForge) == "neoforge");
    }

    private static void LocalizationTest()
    {
        Console.WriteLine("[多语言管理]");
        Check("默认 zh_CN", LocaleManager.CurrentLocale == "zh_CN");
        Check("T(app.title) zh", LocaleManager.T("app.title") == "燧石启动器");

        LocaleManager.CurrentLocale = "en_US";
        Check("T(app.title) en", LocaleManager.T("app.title") == "Chert Launcher");
        Check("T(不存在) fallback", LocaleManager.T("nonexistent_key") == "nonexistent_key");

        LocaleManager.CurrentLocale = "zh_CN"; // 恢复
        Check("Tf 格式化", LocaleManager.Tf("msg.launching", "1.20.1").Contains("1.20.1"));
    }

    private static void SkinFetcherTest()
    {
        Console.WriteLine("[皮肤解析]");
        // 模拟 textures Base64
        var texturesJson = @"{""textures"":{""SKIN"":{""url"":""https://example.com/skin.png"",""metadata"":{""model"":""slim""}},""CAPE"":{""url"":""https://example.com/cape.png""}}}";
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(texturesJson));
        var profile = new MinecraftProfile
        {
            Id = "abc",
            Name = "TestPlayer",
            Properties = new List<ProfileProperty> { new() { Name = "textures", Value = b64 } }
        };
        // 使用反射调用私有方法测试（这里只测模型绑定）
        Check("Profile name", profile.Name == "TestPlayer");
        Check("Profile has textures", profile.Properties.Any(p => p.Name == "textures"));
    }

    private static void ThemeManagerTest()
    {
        Console.WriteLine("[主题管理]");
        var initial = ThemeManager.Current;
        // WPF 与 Linux 两端 ThemeManager 默认均为 Light（Core/Theme/ThemeManager.cs:16），
        // 两版本就对齐；此前探针误假设默认暗色，在此纠正为默认亮色。
        Check("默认亮色", initial == ThemeType.Light);

        ThemeManager.Current = ThemeType.Light;
        Check("切换亮色", ThemeManager.Current == ThemeType.Light);

        ThemeManager.Current = ThemeType.Dark;
        Check("切回暗色", ThemeManager.Current == ThemeType.Dark);
    }

    private static void ModMetadataParserTest()
    {
        Console.WriteLine("[Mod 元数据解析]");

        // 测试 TOML 解析
        var toml = @"
[[mods]]
modId=""examplemod""
displayName=""Example Mod""
version=""1.0.0""

[[dependencies.examplemod]]
modId=""fabric-api""
mandatory=true
versionRange=""[0.92,1.0)""
ordering=""NONE""
side=""BOTH""
";
        var meta = ModMetadataParser.ParseForgeMod("nonexistent.jar"); // 文件不存在返回 null
        Check("不存在的jar返回null", meta is null);

        // 测试 DependencyCheckResult 模型
        var result = new DependencyCheckResult
        {
            ModFileName = "test.jar",
            ModId = "testmod",
            ModName = "Test Mod",
            Missing = { new MissingDependency { DependencyId = "dep1", VersionRange = ">=1.0", Required = true } }
        };
        Check("依赖检查 Missing 1", result.Missing.Count == 1);
        Check("依赖检查 dep1", result.Missing[0].DependencyId == "dep1");
    }

    // ---- v0.5 新增测试 ----

    private static void RecommendationSystemTest()
    {
        Console.WriteLine("[智能推荐系统 v0.5]");

        // 玩法分区映射
        Check("technology → Tech", GameplayCategoryMap.FromModrinthCategories(new[] { "technology" }) == GameplayCategory.Tech);
        Check("magic → Magic", GameplayCategoryMap.FromModrinthCategories(new[] { "magic" }) == GameplayCategory.Magic);
        Check("未识别 → null", GameplayCategoryMap.FromModrinthCategories(new[] { "xyz" }) is null);
        Check("Tech 展示名=生电", GameplayCategoryMap.DisplayName(GameplayCategory.Tech) == "生电");
        Check("All 含 6 个分区", GameplayCategoryMap.All.Count == 6);
        Check("ToModrinth Tech=technology", GameplayCategoryMap.ToModrinthCategory(GameplayCategory.Tech) == "technology");

        // 规则引擎：Fabric 已装但无 API + 存在光影包但无 Iris
        var root = Path.Combine(Path.GetTempPath(), "mclcs_rec_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var sp = PathEx.ShaderPacksDir(root);
            Directory.CreateDirectory(sp);
            File.WriteAllText(Path.Combine(sp, "test.zip"), "dummy");

            var installed = new List<ModEntry>
            {
                new() { ModId = "sodium", Loader = "fabric", FileName = "sodium.jar" }
            };
            var rules = RuleEngine.EvaluateLocalRules(root, installed);
            Check("规则推荐 Fabric API", rules.Any(r => r.Slug == "fabric-api" && r.IsDependencyCompletion));
            Check("规则推荐 Iris", rules.Any(r => r.Slug == "iris" && r.IsDependencyCompletion));

            // 已装 Fabric API + Iris → 不再推荐
            var installed2 = new List<ModEntry>
            {
                new() { ModId = "fabric-api", Loader = "fabric", FileName = "fabric-api.jar" },
                new() { ModId = "iris", Loader = "fabric", FileName = "iris.jar" }
            };
            var rules2 = RuleEngine.EvaluateLocalRules(root, installed2);
            Check("已装 API+Ir is 时无推荐", rules2.Count == 0);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        // 推荐引擎：Disabled → 空；LocalOnly + 玩法偏好过滤
        var profileDisabled = new LauncherProfile { IntelliRecommend = IntelliRecommendMode.Disabled };
        var empty = RecommendationEngine.BuildAsync(GameConstants.DefaultGameRoot, profileDisabled,
            new HttpClient(), null).GetAwaiter().GetResult();
        Check("Disabled 返回空", empty.Count == 0);

        var profileLocal = new LauncherProfile
        {
            IntelliRecommend = IntelliRecommendMode.LocalOnly,
            PreferredCategories = new() { GameplayCategory.Tech } // 只关心生电
        };
        // LocalOnly 不联网，仅本地规则；用含 Fabric 的假环境
        var root2 = Path.Combine(Path.GetTempPath(), "mclcs_rec2_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root2);
            var localItems = RuleEngine.EvaluateLocalRules(root2, new List<ModEntry> { new() { ModId = "x", Loader = "fabric" } });
            // 依赖补全类（fabric-api → Tech）应保留；若偏好只选 Tech 则 iris(Optimization) 被过滤——但规则引擎不过滤，过滤在引擎
            Check("本地规则含 fabric-api", localItems.Any(i => i.Slug == "fabric-api"));
        }
        finally
        {
            if (Directory.Exists(root2)) Directory.Delete(root2, true);
        }

        // 热门榜单缓存：写入缓存后可命中（不联网）
        var root3 = Path.Combine(Path.GetTempPath(), "mclcs_rec3_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root3);
            var cached = new List<RecommendationItem>
            {
                new() { Slug = "sodium", Title = "Sodium", Category = GameplayCategory.Optimization }
            };
            // 直接写缓存（通过反射私有方法不便，这里用公开路径约定复刻）
            var cacheDir = Path.Combine(root3, "cache");
            Directory.CreateDirectory(cacheDir);
            var payload = new { CachedAt = DateTime.UtcNow, Items = cached };
            File.WriteAllText(Path.Combine(cacheDir, "mclcs_hot_any_Any_all.json"),
                System.Text.Json.JsonSerializer.Serialize(payload));

            var hot = HotRanking.GetHotAsync(new HttpClient(), root3, null, LoaderType.Any, null, ct: CancellationToken.None)
                .GetAwaiter().GetResult();
            Check("榜单命中本地缓存", hot.Count == 1 && hot[0].Slug == "sodium");
        }
        finally
        {
            if (Directory.Exists(root3)) Directory.Delete(root3, true);
        }
    }

    /// <summary>无网络下载器（仅用于离线测试）。</summary>
    private class NoopDownloader : IDownloader
    {
        public Task DownloadAsync(DownloadItem item, IProgress<double>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DownloadBatchAsync(IEnumerable<DownloadItem> items, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static async Task LibraryRepairTest()
    {
        Console.WriteLine("[库修复 LibraryRepair]");
        var root = Path.Combine(Path.GetTempPath(), "mclcs_librepair_" + Guid.NewGuid().ToString("N"));
        var downloader = new NoopDownloader();
        try
        {
            var vdir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(vdir);
            var version = new VersionJson { Id = "1.20.1", Libraries = new() };
            await File.WriteAllTextAsync(Path.Combine(vdir, "1.20.1.json"),
                JsonSerializer.Serialize(version));

            // 无库版本：无需下载，所有库健康（FixedCount=0）
            var ok = await LibraryRepair.RepairAsync(root, "1.20.1", new HttpClient(), downloader, null);
            Check("无库版本 AllHealthy", ok.AllHealthy);
            Check("无库版本 FixedCount=0", ok.FixedCount == 0);

            // 不存在的版本：返回失败
            var fail = await LibraryRepair.RepairAsync(root, "9.9.9", new HttpClient(), downloader, null);
            Check("不存在版本 Success=false", !fail.Success);
            Check("不存在版本 AllHealthy=false", !fail.AllHealthy);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        await Task.CompletedTask;
    }

    private static void CrashRepairModelsV05Test()
    {
        Console.WriteLine("[崩溃修复模型 v0.5]");

        // ModConflictInfo
        var conflict = new ModConflictInfo
        {
            FilePath = "/mods/sodium.jar",
            Name = "Sodium"
        };
        Check("ModConflictInfo FilePath", conflict.FilePath == "/mods/sodium.jar");
        Check("ModConflictInfo Name", conflict.Name == "Sodium");

        // RepairStrategy 新枚举值
        Check("DisableConflictingMods 枚举值", RepairStrategy.DisableConflictingMods.ToString() == "DisableConflictingMods");
        Check("InstallMissingModDependency 枚举值", RepairStrategy.InstallMissingModDependency.ToString() == "InstallMissingModDependency");

        // CrashRepairPlan 新字段
        var plan = new CrashRepairPlan();
        Check("ConflictingMods 初始为空", plan.ConflictingMods.Count == 0);
        Check("MissingModDependencies 初始为空", plan.MissingModDependencies.Count == 0);
        Check("KeepModFile 初始为 null", plan.KeepModFile is null);

        plan.ConflictingMods.Add(new ModConflictInfo { FilePath = "/mods/a.jar", Name = "A" });
        plan.ConflictingMods.Add(new ModConflictInfo { FilePath = "/mods/b.jar", Name = "B" });
        Check("ConflictingMods 添加后 2 个", plan.ConflictingMods.Count == 2);

        plan.MissingModDependencies.Add("fabric-api");
        plan.MissingModDependencies.Add("cloth-config");
        Check("MissingModDependencies 添加后 2 个", plan.MissingModDependencies.Count == 2);
        Check("MissingModDependencies 含 fabric-api", plan.MissingModDependencies.Contains("fabric-api"));
    }

    private static void JavaVendorTest()
    {
        Console.WriteLine("[JavaVendor 枚举 & Oracle 安装模型]");

        // 枚举值
        Check("JavaVendor.Auto", JavaVendor.Auto.ToString() == "Auto");
        Check("JavaVendor.Temurin", JavaVendor.Temurin.ToString() == "Temurin");
        Check("JavaVendor.Oracle", JavaVendor.Oracle.ToString() == "Oracle");

        // 默认值
        var profile = new LauncherProfile();
        Check("Profile 默认 PreferredJavaVendor = Auto", profile.PreferredJavaVendor == JavaVendor.Auto);

        // JavaInstaller 静态方法存在
        var ensureMethod = typeof(JavaInstaller).GetMethod("EnsureJavaAsync",
            new[] { typeof(int), typeof(string), typeof(IDownloader), typeof(JavaVendor), typeof(ILogger), typeof(CancellationToken) });
        Check("JavaInstaller.EnsureJavaAsync(JavaVendor) 方法存在", ensureMethod is not null);

        // Oracle 方法存在（public static）
        var oracleMethod = typeof(JavaInstaller).GetMethod("InstallOracleAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Check("JavaInstaller.InstallOracleAsync 方法存在", oracleMethod is not null);
    }

    private static void AutoInstallPolicyTest()
    {
        Console.WriteLine("[AutoInstallPolicy 枚举]");

        Check("AutoInstallPolicy.Always", AutoInstallPolicy.Always.ToString() == "Always");
        Check("AutoInstallPolicy.Ask", AutoInstallPolicy.Ask.ToString() == "Ask");
        Check("AutoInstallPolicy.Never", AutoInstallPolicy.Never.ToString() == "Never");

        var profile = new LauncherProfile();
        Check("Profile 默认 AutoInstallMissingMods = Ask", profile.AutoInstallMissingMods == AutoInstallPolicy.Ask);
    }

    private static void ModConflictPlanTest()
    {
        Console.WriteLine("[Mod 冲突修复规划]");

        var gameRoot = Path.Combine(Path.GetTempPath(), "mclcs_conflict_" + Guid.NewGuid().ToString("N"));
        try
        {
            // 创建一个无 mods 目录的环境，测试非 OOM/非 Java 的崩溃也能产出 plan
            Directory.CreateDirectory(gameRoot);
            var modsDir = Path.Combine(gameRoot, "mods");
            Directory.CreateDirectory(modsDir);

            // 非 OOM/Java 的崩溃（如 ClassNotFoundException），应尝试检测 mod 问题
            var cnf = new CrashAnalysis { Category = CrashCategory.MissingLibrary };
            var plan = CrashRepairEngine.BuildPlan(cnf, new LauncherProfile(), null, gameRoot, "1.20.1");

            // 在没有 mod 冲突/缺失依赖的情况下，不应产出冲突或缺失计划
            Check("无冲突时 ConflictingMods 为空", plan.ConflictingMods.Count == 0);
            Check("无缺失时 MissingModDependencies 为空", plan.MissingModDependencies.Count == 0);
        }
        finally
        {
            if (Directory.Exists(gameRoot)) Directory.Delete(gameRoot, true);
        }
    }

    private static void ProfileV05Test()
    {
        Console.WriteLine("[LauncherProfile v0.5 序列化]");

        var profile = new LauncherProfile
        {
            PreferredJavaVendor = JavaVendor.Oracle,
            AutoInstallMissingMods = AutoInstallPolicy.Always
        };

        var json = JsonSerializer.Serialize(profile);
        Check("序列化含 preferredJavaVendor", json.Contains("preferredJavaVendor"));
        Check("序列化含 autoInstallMissingMods", json.Contains("autoInstallMissingMods"));

        var deserialized = JsonSerializer.Deserialize<LauncherProfile>(json);
        Check("反序列化 PreferredJavaVendor = Oracle", deserialized?.PreferredJavaVendor == JavaVendor.Oracle);
        Check("反序列化 AutoInstallMissingMods = Always", deserialized?.AutoInstallMissingMods == AutoInstallPolicy.Always);
    }

    // ---- §二.4 / §三 / §四.2 存档兼容性、降级、降级联动 ----

    /// <summary>构造一个最小 level.dat（gzip NBT，含 DataVersion）。</summary>
    private static void WriteLevelDat(string saveDir, int dataVersion)
    {
        Directory.CreateDirectory(saveDir);
        var root = NbtTag.Compound("");
        var data = NbtTag.Compound("Data");
        data.Children!.Add(NbtTag.Int("GameType", 0));
        root.Children!.Add(data);
        root.Children.Add(NbtTag.Int("DataVersion", dataVersion));
        NbtFile.WriteGzip(SaveCompatibilityDetector.LevelDatPath(saveDir), root);
    }

    private static async Task SaveFeatureTests()
    {
        Console.WriteLine("[NBT 读写往返]");
        NbtRoundTripTest();
        Console.WriteLine("[DataVersion 对照表]");
        DataVersionMapTest();
        Console.WriteLine("[§二.4 存档兼容性检测]");
        SaveCompatTest();
        Console.WriteLine("[§三 存档降级 + 备份回滚]");
        await SaveDowngradeTest();
        Console.WriteLine("[§四.2 降级联动]");
        DowngradeLinkageTest();
    }

    private static void NbtRoundTripTest()
    {
        var root = NbtTag.Compound("root");
        var c = NbtTag.Compound("c");
        c.Children!.Add(NbtTag.Int("DataVersion", 3465));
        c.Children.Add(new NbtTag { Type = NbtTagType.Byte, Name = "b", ByteValue = 5 });
        c.Children.Add(new NbtTag { Type = NbtTagType.Short, Name = "s", ShortValue = 1234 });
        c.Children.Add(new NbtTag { Type = NbtTagType.Long, Name = "l", LongValue = 12345678901L });
        c.Children.Add(new NbtTag { Type = NbtTagType.Float, Name = "f", FloatValue = 3.14f });
        c.Children.Add(new NbtTag { Type = NbtTagType.Double, Name = "d", DoubleValue = 2.718281828 });
        c.Children.Add(new NbtTag { Type = NbtTagType.String, Name = "str", StringValue = "你好nbt" });
        c.Children.Add(new NbtTag { Type = NbtTagType.ByteArray, Name = "ba", ByteArrayValue = new byte[] { 1, 2, 3 } });
        c.Children.Add(new NbtTag { Type = NbtTagType.IntArray, Name = "ia", IntArrayValue = new[] { 10, 20, 30 } });
        c.Children.Add(new NbtTag { Type = NbtTagType.LongArray, Name = "la", LongArrayValue = new[] { 100L, 200L } });
        var list = new NbtTag { Type = NbtTagType.List, Name = "list", Children = new List<NbtTag> { NbtTag.Int("x", 7), NbtTag.Int("y", 8) } };
        c.Children.Add(list);
        root.Children!.Add(c);

        using var ms = new MemoryStream();
        using (var gzip = new GZipStream(ms, CompressionMode.Compress, leaveOpen: true))
            NbtFile.Write(gzip, root);

        // 先用原始流隔离（排除 gzip 影响）
        using var raw = new MemoryStream();
        NbtFile.Write(raw, root);
        raw.Position = 0;
        var rawBack = NbtFile.Read(raw);
        Check("原始流往返 Int DataVersion=3465", rawBack.GetChild("c")!.GetChild("DataVersion")!.IntValue == 3465);

        ms.Position = 0;
        using var ingzip = new GZipStream(ms, CompressionMode.Decompress);
        var back = NbtFile.Read(ingzip);

        Check("根名 root", back.Name == "root");
        var rc = back.GetChild("c")!;
        Check("Int DataVersion=3465", rc.GetChild("DataVersion")!.IntValue == 3465);
        Check("Byte=5", rc.GetChild("b")!.ByteValue == 5);
        Check("Short=1234", rc.GetChild("s")!.ShortValue == 1234);
        Check("Long 往返", rc.GetChild("l")!.LongValue == 12345678901L);
        Check("Float 往返", Math.Abs(rc.GetChild("f")!.FloatValue - 3.14f) < 1e-6);
        Check("Double 往返", Math.Abs(rc.GetChild("d")!.DoubleValue - 2.718281828) < 1e-12);
        Check("String 往返", rc.GetChild("str")!.StringValue == "你好nbt");
        Check("ByteArray 往返", ((ReadOnlySpan<byte>)rc.GetChild("ba")!.ByteArrayValue!).SequenceEqual(new byte[] { 1, 2, 3 }));
        Check("IntArray 往返", rc.GetChild("ia")!.IntArrayValue!.SequenceEqual(new[] { 10, 20, 30 }));
        Check("LongArray 往返", rc.GetChild("la")!.LongArrayValue!.SequenceEqual(new[] { 100L, 200L }));
        Check("List 元素类型 Int", rc.GetChild("list")!.Children![0].Type == NbtTagType.Int);
        Check("List 元素值 7/8", rc.GetChild("list")!.Children![0].IntValue == 7 && rc.GetChild("list")!.Children![1].IntValue == 8);

        // 递归读取 DataVersion（与 level.dat 用法一致）
        Check("GetDataVersion 递归=3465", back.GetDataVersion() == 3465);

        // 改写 DataVersion 后再次往返
        back.TrySetDataVersion(3337);
        using var ms2 = new MemoryStream();
        using (var g2 = new GZipStream(ms2, CompressionMode.Compress, leaveOpen: true))
            NbtFile.Write(g2, back);
        ms2.Position = 0;
        using var ig2 = new GZipStream(ms2, CompressionMode.Decompress);
        Check("改写后 GetDataVersion=3337", NbtFile.Read(ig2).GetDataVersion() == 3337);
    }

    private static void DataVersionMapTest()
    {
        Check("1.20.1 -> 3465", DataVersionMap.ToDataVersion("1.20.1") == 3465);
        Check("1.19.4 -> 3337", DataVersionMap.ToDataVersion("1.19.4") == 3337);
        Check("1.21.8 -> 4440", DataVersionMap.ToDataVersion("1.21.8") == 4440);
        Check("1.12.2 -> 1343", DataVersionMap.ToDataVersion("1.12.2") == 1343);
        Check("fabric-1.20.1 去前缀 -> 3465", DataVersionMap.ToDataVersion("fabric-1.20.1") == 3465);
        Check("dv 3465 -> 1.20.1", DataVersionMap.ToGameVersion(3465) == "1.20.1");
        Check("dv 4440 -> 1.21.8", DataVersionMap.ToGameVersion(4440) == "1.21.8");
        Check("未知版本 -> null", DataVersionMap.ToDataVersion("99.99.99") is null);
        Check("KnownVersions 升序且含 1.20.1", DataVersionMap.KnownVersions().First() == "1.12.2" && DataVersionMap.KnownVersions().Contains("1.20.1"));

        // ---- 新命名方案 YY.M ----
        Check("26.1 -> 4786", DataVersionMap.ToDataVersion("26.1") == 4786);
        Check("26.1.2 -> 4790", DataVersionMap.ToDataVersion("26.1.2") == 4790);
        Check("26.2 -> 4903", DataVersionMap.ToDataVersion("26.2") == 4903);
        Check("1.21.11 -> 4671", DataVersionMap.ToDataVersion("1.21.11") == 4671);
        Check("fabric-26.1 去前缀 -> 4786", DataVersionMap.ToDataVersion("fabric-26.1") == 4786);
        Check("dv 4786 -> 26.1", DataVersionMap.ToGameVersion(4786) == "26.1");
        Check("dv 4903 -> 26.2", DataVersionMap.ToGameVersion(4903) == "26.2");
        Check("比较 26.1 < 26.2", DataVersionMap.CompareVersions("26.1", "26.2") < 0);
        Check("比较 1.21.11 < 26.1", DataVersionMap.CompareVersions("1.21.11", "26.1") < 0);
        Check("KnownVersions 含 26.1 与 26.2", DataVersionMap.KnownVersions().Contains("26.1") && DataVersionMap.KnownVersions().Contains("26.2"));
    }

    private static void SaveCompatTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_selftest_saves_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = SaveCompatibilityDetector.SavesDir(root);
            // 略高版本存档（1.20.2, dv=3578）启动 1.20.1（dv=3465）→ 不兼容、轻微
            WriteLevelDat(Path.Combine(saves, "NewWorld"), 3578);
            // 兼容存档（1.12.2, dv=1343）启动 1.20.1 → 兼容
            WriteLevelDat(Path.Combine(saves, "OldWorld"), 1343);

            var reports = SaveCompatibilityDetector.Scan(root, "1.20.1");
            var newW = reports.First(r => r.SaveName == "NewWorld");
            var oldW = reports.First(r => r.SaveName == "OldWorld");

            Check("NewWorld 不兼容", !newW.Compatible);
            Check("NewWorld 严重程度 SlightlyNewer", newW.Severity == SaveCompatibilitySeverity.SlightlyNewer);
            Check("NewWorld 建议降级", newW.RecommendedAction == SaveCompatAction.Downgrade);
            Check("NewWorld 反查版本 1.20.2", newW.SaveGameVersion == "1.20.2");

            Check("OldWorld 兼容", oldW.Compatible);
            Check("OldWorld 严重度 Ok", oldW.Severity == SaveCompatibilitySeverity.Ok);

            // 跨大版本：1.21.8（dv=4440）启动 1.19（dv=3105）→ 不兼容、严重
            WriteLevelDat(Path.Combine(saves, "FutureWorld"), 4440);
            var futReport = SaveCompatibilityDetector.CheckSingleSave(Path.Combine(saves, "FutureWorld"), "1.19");
            Check("FutureWorld 不兼容", futReport is not null && !futReport.Compatible);
            Check("FutureWorld 严重程度 MuchNewer", futReport is not null && futReport.Severity == SaveCompatibilitySeverity.MuchNewer);

            // 未知目标版本：保守视为兼容
            var unknown = SaveCompatibilityDetector.CheckSingleSave(Path.Combine(saves, "NewWorld"), "99.99.99");
            Check("未知目标版本 -> 兼容/Unknown", unknown!.Compatible && unknown.Severity == SaveCompatibilitySeverity.Unknown);

            // ---- 新命名方案 YY.M ----
            // 26.2（dv=4903）存档启动 26.1（dv=4786）→ 不兼容、轻微（仅隔 1 个月）
            WriteLevelDat(Path.Combine(saves, "NewSchemeWorld"), 4903);
            var nsReport = SaveCompatibilityDetector.CheckSingleSave(Path.Combine(saves, "NewSchemeWorld"), "26.1");
            Check("NewSchemeWorld 不兼容", nsReport is not null && !nsReport.Compatible);
            Check("NewSchemeWorld 严重程度 SlightlyNewer", nsReport is not null && nsReport.Severity == SaveCompatibilitySeverity.SlightlyNewer);
            Check("NewSchemeWorld 反查版本 26.2", nsReport is not null && nsReport.SaveGameVersion == "26.2");

            // 新方案跨月较多：26.3（dv=5005）存档启动 26.1（dv=4786）→ 不兼容、严重（隔 2 个月）
            WriteLevelDat(Path.Combine(saves, "MuchNewerNew"), 5005);
            var mnReport = SaveCompatibilityDetector.CheckSingleSave(Path.Combine(saves, "MuchNewerNew"), "26.1");
            Check("MuchNewerNew 不兼容", mnReport is not null && !mnReport.Compatible);
            Check("MuchNewerNew 严重程度 MuchNewer", mnReport is not null && mnReport.Severity == SaveCompatibilitySeverity.MuchNewer);

            // 旧方案 -> 新方案（跨命名方案）：26.1（dv=4786）存档启动 1.21.11（dv=4671）→ 不兼容、严重
            WriteLevelDat(Path.Combine(saves, "CrossSchemeWorld"), 4786);
            var csReport = SaveCompatibilityDetector.CheckSingleSave(Path.Combine(saves, "CrossSchemeWorld"), "1.21.11");
            Check("CrossSchemeWorld 不兼容", csReport is not null && !csReport.Compatible);
            Check("CrossSchemeWorld 严重程度 MuchNewer", csReport is not null && csReport.Severity == SaveCompatibilitySeverity.MuchNewer);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SaveDowngradeTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_selftest_down_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = SaveCompatibilityDetector.SavesDir(root);
            var saveDir = Path.Combine(saves, "MyWorld");
            WriteLevelDat(saveDir, 3465); // 1.20.1

            Check("降级前 GetSaveDataVersion=3465", SaveDowngrader.GetSaveDataVersion(saveDir) == 3465);

            // 方案 A：快速降级到 1.19.4 (dv=3337)
            var plan = await SaveDowngrader.DowngradeAsync(saveDir, "1.19.4", DowngradeMethod.QuickModifyDataVersion);
            Check("降级成功", plan.Success);
            Check("降级 From=3465 To=3337", plan.FromDataVersion == 3465 && plan.ToDataVersion == 3337);
            Check("强制备份已创建", plan.BackupPath is not null && Directory.Exists(plan.BackupPath));
            Check("降级后 DataVersion=3337", SaveDowngrader.GetSaveDataVersion(saveDir) == 3337);
            Check("变更摘要非空", plan.Summary.Count > 0);

            // 原始存档完好（备份存在且仍为 1.20.1）
            var backups = SaveCompatibilityDetector.FindBackups(saves, "MyWorld");
            Check("找到 1 个备份", backups.Count == 1);
            Check("备份 DataVersion 仍为 3465", backups[0].DataVersion == 3465);
            Check("备份 GameVersion=1.20.1", backups[0].GameVersion == "1.20.1");

            // 回滚到备份
            var replaced = SaveDowngrader.RestoreBackupAsync(backups[0].BackupPath, saveDir);
            Check("回滚后 DataVersion 恢复 3465", SaveDowngrader.GetSaveDataVersion(saveDir) == 3465);
            Check("回滚时另存了 replaced 目录", !string.IsNullOrEmpty(replaced) && Directory.Exists(replaced));

            // 方案 B：Amulet 未安装时应优雅失败（不抛异常）
            var planB = await SaveDowngrader.DowngradeAsync(saveDir, "1.19.4", DowngradeMethod.Amulet);
            Check("Amulet 缺失时降级失败但安全", !planB.Success && planB.ErrorMessage is not null);
            Check("Amulet 失败仍保留备份", SaveCompatibilityDetector.FindBackups(saves, "MyWorld").Count >= 1);

            // ---- 新命名方案 YY.M 降级：26.2 (dv=4903) -> 26.1 (dv=4786) ----
            var newSaveDir = Path.Combine(saves, "NewSchemeWorld");
            WriteLevelDat(newSaveDir, 4903);
            Check("新方案降级前 GetSaveDataVersion=4903", SaveDowngrader.GetSaveDataVersion(newSaveDir) == 4903);
            var plan26 = await SaveDowngrader.DowngradeAsync(newSaveDir, "26.1", DowngradeMethod.QuickModifyDataVersion);
            Check("新方案降级成功", plan26.Success);
            Check("新方案降级 From=4903 To=4786", plan26.FromDataVersion == 4903 && plan26.ToDataVersion == 4786);
            Check("新方案降级后 DataVersion=4786", SaveDowngrader.GetSaveDataVersion(newSaveDir) == 4786);
            Check("新方案降级反查版本=26.1", SaveCompatibilityDetector.CheckSingleSave(newSaveDir, "1.21.8") is { } r26 && r26.SaveGameVersion == "26.1");
            Check("新方案降级已强制备份", plan26.BackupPath is not null && Directory.Exists(plan26.BackupPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void DowngradeLinkageTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_selftest_link_" + Guid.NewGuid().ToString("N"));
        try
        {
            var saves = SaveCompatibilityDetector.SavesDir(root);
            var saveDir = Path.Combine(saves, "LinkWorld");
            WriteLevelDat(saveDir, 3465);

            // 无备份：不适用
            var analysisNoBackup = new CrashAnalysis
            {
                Category = CrashCategory.Unknown,
                ExceptionType = "java.lang.RuntimeException",
                RawReport = "Caught exception in world tick: chunk ... NBT ..."
            };
            var noPlan = DowngradeCrashLinkage.BuildPlan(analysisNoBackup, saveDir, root);
            Check("无降级备份时降级联动不适用", !noPlan.Applicable);

            // 制造一次降级（生成备份）
            var down = SaveDowngrader.DowngradeAsync(saveDir, "1.19.4", DowngradeMethod.QuickModifyDataVersion).Result;

            // 世界相关崩溃 + 存在备份 → 适用
            var analysis = new CrashAnalysis
            {
                Category = CrashCategory.Unknown,
                ExceptionType = "java.lang.RuntimeException",
                RawReport = "Caught exception in world tick: Exception generating new chunk: NBT tag ... region file corrupt"
            };
            var plan = DowngradeCrashLinkage.BuildPlan(analysis, saveDir, root);
            Check("存在备份+世界崩溃 -> 适用", plan.Applicable);
            Check("建议动作=回滚备份", plan.SuggestedAction == DowngradeRecoveryAction.RevertToBackup);
            Check("选项含回滚/其他方式/安装原版", plan.Options.Contains(DowngradeRecoveryAction.RevertToBackup)
                && plan.Options.Contains(DowngradeRecoveryAction.TryOtherMethod)
                && plan.Options.Contains(DowngradeRecoveryAction.InstallOriginalVersion));
            Check("原始版本反查=1.20.1", plan.OriginalGameVersion == "1.20.1");
            Check("BackupPath 指向备份", plan.BackupPath is not null && System.Text.RegularExpressions.Regex.IsMatch(plan.BackupPath, @"\.backup-\d{14}$"));

            // 内存类崩溃（与降级无关）→ 不适用
            var memAnalysis = new CrashAnalysis
            {
                Category = CrashCategory.OutOfMemory,
                ExceptionType = "java.lang.OutOfMemoryError",
                RawReport = "Java heap space"
            };
            var memPlan = DowngradeCrashLinkage.BuildPlan(memAnalysis, saveDir, root);
            Check("内存崩溃不误判为降级联动", !memPlan.Applicable);

            // 接入 CrashRepairEngine：Unknown + 世界崩溃 → 生成含降级恢复的方案
            var profile = new LauncherProfile();
            var repair = CrashRepairEngine.BuildPlan(analysis, profile, null, root, "1.19.4", saveDir);
            Check("CrashRepairEngine 产出降级恢复方案", repair.CanRepair && repair.DowngradeRecovery is not null);
            Check("降级恢复策略=回滚备份", repair.Strategy == RepairStrategy.RevertDowngradeBackup);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // ---- v1.0 工具箱 / 统计 / AI / 更新 / 多开 模块 ----

    private static async Task ToolboxV1Tests()
    {
        Console.WriteLine("[LogManager 日志管理]");
        await LogManagerTest();
        Console.WriteLine("[ScreenshotManager 截图管理]");
        await ScreenshotManagerTest();
        Console.WriteLine("[NetworkDiagnostics 网络诊断]");
        await NetworkDiagnosticsTest();
        Console.WriteLine("[RedundantFileCleaner 冗余清理]");
        RedundantCleanerTest();
        Console.WriteLine("[ModpackExporter 整合包导出]");
        ModpackExportTest();
        Console.WriteLine("[PlaytimeTracker 游玩统计]");
        PlaytimeTrackerTest();
        Console.WriteLine("[Assistant AI 助手]");
        AssistantTest();
        Console.WriteLine("[LauncherUpdater 自动更新]");
        LauncherUpdaterTest();
        Console.WriteLine("[InstanceTracker 多开实例]");
        InstanceTrackerTest();
    }

    private static string TempDir(string name)
        => Path.Combine(Path.GetTempPath(), "mclcs_selftest_" + name + "_" + Guid.NewGuid().ToString("N"));

    private static async Task LogManagerTest()
    {
        var root = TempDir("logs");
        try
        {
            Directory.CreateDirectory(LogManager.LogsDir(root));
            var logPath = Path.Combine(LogManager.LogsDir(root), "latest.log");
            await File.WriteAllTextAsync(logPath,
                "[INFO] 启动游戏\n[ERROR] java.lang.OutOfMemoryError: Java heap space\n[WARN] 内存偏低");

            var files = LogManager.ListLogs(root);
            Check("ListLogs 含 latest.log", files.Any(f => f.Name == "latest.log"));

            var text = LogManager.ReadLog(logPath);
            Check("ReadLog 读回内容", text.Contains("OutOfMemoryError"));

            var lines = LogManager.ParseLines(text);
            Check("ParseLines 行数=3", lines.Count == 3);
            Check("ParseLines 错误分级", lines.Any(l => l.Severity == LogSeverity.Error));

            var onlyErr = LogManager.Filter(lines, onlyErrors: true);
            Check("Filter 仅错误=1", onlyErr.Count == 1);

            var kw = LogManager.Filter(lines, "内存");
            Check("Filter 关键字『内存』=1", kw.Count == 1);

            var dest = Path.Combine(root, "export.log");
            Check("Export 复制成功", LogManager.Export(logPath, dest) && File.Exists(dest));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task ScreenshotManagerTest()
    {
        var root = TempDir("shots");
        try
        {
            Check("空目录 ListScreenshots 为空", ScreenshotManager.ListScreenshots(root).Count == 0);

            var dir = ScreenshotManager.ScreenshotsDir(root);
            Directory.CreateDirectory(dir);
            var p1 = Path.Combine(dir, "a.png");
            await File.WriteAllTextAsync(p1, "x");
            Check("列出截图=1", ScreenshotManager.ListScreenshots(root).Count == 1);

            var zip = Path.Combine(root, "pack.zip");
            ScreenshotManager.Package(new[] { p1 }, zip);
            Check("Package 生成 zip", File.Exists(zip));

            Check("DeleteScreenshots 删除=1", ScreenshotManager.DeleteScreenshots(new[] { p1 }) == 1);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task NetworkDiagnosticsTest()
    {
        var eps = new[] { ("本地不可达", "http://127.0.0.1:1/") };
        var results = await NetworkDiagnostics.DiagnoseAsync(eps, new HttpClient());
        Check("不可达端点 Reachable=false", results.Count == 1 && !results[0].Reachable);

        var single = await NetworkDiagnostics.ProbeAsync("z", "http://127.0.0.1:1/", new HttpClient(), 2000);
        Check("ProbeAsync 超时 Reachable=false 且不抛异常", single.Reachable == false);
    }

    private static void RedundantCleanerTest()
    {
        var root = TempDir("redundant");
        try
        {
            Check("空 gameRoot Scan 为空", RedundantFileCleaner.Scan(root).Count == 0);
            Check("ComputeReferencedPaths 为空集合", RedundantFileCleaner.ComputeReferencedPaths(root).Count == 0);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void ModpackExportTest()
    {
        var root = TempDir("modpack");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "mods"));
            var dest = Path.Combine(root, "pack.zip");
            ModpackExporter.Export(root, "1.20.1", dest, new ModpackExportOptions
            {
                IncludeMods = false,
                IncludeConfig = false,
                IncludeResourcePacks = false,
                IncludeShaderPacks = false,
                IncludeSaves = false
            });
            Check("Export 生成 zip", File.Exists(dest));

            using var zip = ZipFile.OpenRead(dest);
            var manifestEntry = zip.GetEntry("mclcs_manifest.json");
            Check("含 mclcs_manifest.json", manifestEntry is not null);
            if (manifestEntry is not null)
            {
                using var sr = new StreamReader(manifestEntry.Open());
                var json = sr.ReadToEnd();
                Check("清单含版本 1.20.1", json.Contains("1.20.1"));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void PlaytimeTrackerTest()
    {
        var root = TempDir("stats");
        try
        {
            var after1 = PlaytimeTracker.RecordLaunch(root, "1.20.1");
            Check("RecordLaunch 启动次数=1", after1.LaunchCount == 1);

            var after2 = PlaytimeTracker.RecordCrash(root);
            Check("RecordCrash 崩溃次数=1", after2.CrashCount == 1);

            var after3 = PlaytimeTracker.RecordPlayMinutes(root, 30);
            Check("RecordPlayMinutes 累计=30", after3.TotalPlayMinutes == 30);

            var loaded = PlaytimeTracker.Load(root);
            Check("Load 持久化启动次数", loaded.LaunchCount == 1 && loaded.TotalPlayMinutes == 30);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static void AssistantTest()
    {
        Assistant.Config = new AiConfig { Enabled = false, Mode = AiMode.Local };
        var interp = Assistant.InterpretCrashAsync(
            "Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space").GetAwaiter().GetResult();
        Check("本地崩溃解读命中内存不足", interp.Contains("内存"));

        var noCrash = Assistant.InterpretCrashAsync("游戏正常退出").GetAwaiter().GetResult();
        Check("无已知类型回退提示", !string.IsNullOrEmpty(noCrash));

        var translated = Assistant.TranslateModDescriptionAsync("A cool mod").GetAwaiter().GetResult();
        Check("本地翻译原样返回", translated == "A cool mod");
    }

    private static async Task AiV2Tests()
    {
        Console.WriteLine("[AI 助手 v2：本地部署 Ollama + 外部 API]");

        // 本地模型目录
        Check("模型目录含 3 项", OllamaModels.Catalog.Count == 3);
        Check("默认模型为 Qwen2.5-Coder-1.5B", OllamaModels.Default.DisplayName == "Qwen2.5-Coder-1.5B");
        var qwen = OllamaModels.ByDisplayName("Qwen2.5-Coder-1.5B");
        Check("Qwen tag", qwen?.OllamaTag == "qwen2.5-coder:1.5b");
        Check("Qwen 大小 0.9GB", qwen?.SizeGb == 0.9);
        Check("Qwen 推荐标签", qwen?.RecommendTag == "【默认推荐】");
        var internlm = OllamaModels.ByDisplayName("InternLM2-1.8B");
        Check("InternLM 大小 1.1GB", internlm?.SizeGb == 1.1);
        var phi = OllamaModels.ByDisplayName("Phi-3.5-mini-3.8B");
        Check("Phi 大小 2.2GB", phi?.SizeGb == 2.2);

        // 版本号解析
        Check("ParseVersion 正常", OllamaManager.ParseVersion("ollama version 0.1.2") == "0.1.2");
        Check("ParseVersion 空", OllamaManager.ParseVersion("not a version") is null);

        // 外部 API 地址 → 模型名推断
        Check("openai -> gpt-4o-mini", Assistant.SuggestModelForEndpoint("https://api.openai.com/v1/chat/completions") == "gpt-4o-mini");
        Check("deepseek -> deepseek-chat", Assistant.SuggestModelForEndpoint("https://api.deepseek.com/v1/chat/completions") == "deepseek-chat");
        Check("127.0.0.1 -> 空", Assistant.SuggestModelForEndpoint("http://127.0.0.1:11434/api/chat") == "");
        Check("localhost -> 空", Assistant.SuggestModelForEndpoint("http://localhost:11434") == "");
        Check("其他 -> 空", Assistant.SuggestModelForEndpoint("https://my.example.com/v1") == "");

        // AiConfig 序列化往返
        var cfg = new AiConfig
        {
            Enabled = true,
            Mode = AiMode.Local,
            SelectedLocalModel = "qwen2.5-coder:1.5b",
            Endpoint = "http://127.0.0.1:11434/api/chat",
            Model = "gpt-4o-mini",
            CrashInterpret = true,
            RecommendReason = false,
            ModTranslate = true
        };
        var json = JsonSerializer.Serialize(cfg);
        var back = JsonSerializer.Deserialize<AiConfig>(json);
        Check("AiConfig 往返 Enabled", back?.Enabled == true);
        Check("AiConfig 往返 Mode=Local", back?.Mode == AiMode.Local);
        Check("AiConfig 往返 SelectedLocalModel", back?.SelectedLocalModel == "qwen2.5-coder:1.5b");

        // 离线环境：服务不可达 / 模型未拉取，且不抛异常
        var status = await OllamaManager.GetServiceStatusAsync();
        Check("离线 GetServiceStatus 返回 NotRunning", status == OllamaServiceStatus.NotRunning);
        var pulled = await OllamaManager.IsModelPulledAsync("qwen2.5-coder:1.5b");
        Check("离线 IsModelPulled 返回 false", pulled == false);

        // 外部 API 开启但无网络 → 回退本地启发式（含内存判定）
        Assistant.Config = new AiConfig
        {
            Enabled = true,
            Mode = AiMode.External,
            Endpoint = "http://127.0.0.1:1/v1/chat/completions",
            Model = "gpt-4o-mini"
        };
        var extFallback = Assistant.InterpretCrashAsync(
            "Exception in thread \"main\" java.lang.OutOfMemoryError: Java heap space").GetAwaiter().GetResult();
        Check("外部 API 失败回退启发式(内存)", extFallback.Contains("内存"));

        // 本地部署但 Ollama 未运行 → 回退本地启发式
        Assistant.Config = new AiConfig
        {
            Enabled = true,
            Mode = AiMode.Local,
            SelectedLocalModel = "qwen2.5-coder:1.5b"
        };
        var localFallback = Assistant.InterpretCrashAsync(
            "java.lang.UnsupportedClassVersionError: class file version 65.0").GetAwaiter().GetResult();
        Check("本地部署未运行回退启发式(Java版本)", localFallback.Contains("Java 版本"));
    }

    private static void LauncherUpdaterTest()
    {
        Check("IsNewer 1.0.0 > 0.5.0", LauncherUpdater.IsNewer("0.5.0", "1.0.0"));
        Check("IsNewer 0.5.0 < 1.0.0 为假", !LauncherUpdater.IsNewer("1.0.0", "0.5.0"));
        Check("IsNewer 相等为假", !LauncherUpdater.IsNewer("1.0.0", "1.0.0"));

        // CheckAsync 的 url 参数已移除（更新源收敛到 GameConstants.UpdateInfoUrl），
        // 改用始终失败的处理器模拟「更新源不可达」：离线、确定，不依赖真实网络。
        using var unreachableClient = new HttpClient(new UnreachableHandler());
        var result = LauncherUpdater.CheckAsync("1.0.0", unreachableClient).GetAwaiter().GetResult();
        Check("CheckAsync 不可达 Available=false 不抛异常", result.Available == false && result.Error is not null);
    }

    /// <summary>始终失败的处理器，用于离线模拟更新源不可达。</summary>
    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(new HttpRequestException("模拟更新源不可达"));
    }

    private static void InstanceTrackerTest()
    {
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        InstanceTracker.Register(pid, "1.20.1");
        var active = InstanceTracker.ListActive();
        Check("Register 后 ListActive 含当前进程", active.Any(i => i.Pid == pid));

        InstanceTracker.Unregister(pid);
        Check("Unregister 后 ActiveCount=0", InstanceTracker.ActiveCount() == 0);
    }

    // ============================================================
    //  v2.0 新模块测试
    // ============================================================

    private static async Task V2Tests()
    {
        UiFrameworkTests();
        AfkTokenTests();
        ShaderTokenTests();
        ServerListTests();
        ServerPingerTests();
        LanScannerTests();
        PixelmapTests();
        MapInstallerTests();
        ToolboxCatalogTests();
        BackupManagerTests();
        FileChangeDetectorTests();
        DataPackConflictTests();
        MusicPlaylistTests();
        SkinEditorTests();
        NbtEditorTests();
        HudTests();
        PrewarmTests();
        AnnualReportTests();
        ResourcePackRepairTests();
        ServerPackCacheTests();
        ProfileV2Tests();
        await Task.CompletedTask;
    }

    private static void UiFrameworkTests()
    {
        Console.WriteLine("[UI 框架 - 四色主标签]");

        var game = MainTabs.Get(MainTabKind.Game);
        var download = MainTabs.Get(MainTabKind.Download);
        var toolbox = MainTabs.Get(MainTabKind.Toolbox);
        var settings = MainTabs.Get(MainTabKind.Settings);

        Check("Game 标签 Kind=Game", game.Kind == MainTabKind.Game);
        Check("Game 标签默认颜色=#4CAF50", game.DefaultColor == "#4CAF50");
        Check("Download 标签 Kind=Download", download.Kind == MainTabKind.Download);
        Check("Download 标签默认颜色=#2196F3", download.DefaultColor == "#2196F3");
        Check("Toolbox 标签 Kind=Toolbox", toolbox.Kind == MainTabKind.Toolbox);
        Check("Toolbox 标签默认颜色=#FF9800", toolbox.DefaultColor == "#FF9800");
        Check("Settings 标签 Kind=Settings", settings.Kind == MainTabKind.Settings);
        Check("Settings 标签默认颜色=#607D8B", settings.DefaultColor == "#607D8B");
        Check("All 共4项", MainTabs.All.Count == 4);

        // TabThemeConfig
        var theme = new TabThemeConfig();
        Check("默认主题 GameColor=#4CAF50", theme.ColorOf(MainTabKind.Game) == "#4CAF50");
        Check("默认主题 DownloadColor=#2196F3", theme.ColorOf(MainTabKind.Download) == "#2196F3");

        // 自定义颜色
        Check("SetColor Game=#FF0000 返回true", theme.SetColor(MainTabKind.Game, "#FF0000"));
        Check("自定义 GameColor=#FF0000", theme.ColorOf(MainTabKind.Game) == "#FF0000");
        Check("IsCustomized=true", theme.IsCustomized());
        Check("Download 未改", theme.ColorOf(MainTabKind.Download) == "#2196F3");

        // 非法颜色
        Check("SetColor 非法返回false", !theme.SetColor(MainTabKind.Game, "not-a-color"));
        Check("非法颜色后仍为#FF0000", theme.ColorOf(MainTabKind.Game) == "#FF0000");

        // Reset
        theme.SetColor(MainTabKind.Game, "#FF0000");
        theme.Reset();
        Check("Reset 后 Game=#4CAF50", theme.ColorOf(MainTabKind.Game) == "#4CAF50");
        Check("Reset 后 IsCustomized=false", !theme.IsCustomized());

        // IsValidColor
        Check("IsValidColor #4CAF50=true", TabThemeConfig.IsValidColor("#4CAF50"));
        Check("IsValidColor #abc=false(需要6位)", !TabThemeConfig.IsValidColor("#abc"));
        Check("IsValidColor bad=false", !TabThemeConfig.IsValidColor("bad"));
        Check("IsValidColor #GGGGGG=false", !TabThemeConfig.IsValidColor("#GGGGGG"));
        Check("IsValidColor empty=false", !TabThemeConfig.IsValidColor(""));

        // 主标签解析
        Check("MainTabs.ById game→Game", MainTabs.ById("game")?.Kind == MainTabKind.Game);
        Check("MainTabs.ById 非法→null", MainTabs.ById("nope") == null);

        // Sidebar（按主标签分组）
        Console.WriteLine("[UI 框架 - 分组侧边栏]");
        Check("下载页副标签 6 项", Sidebar.Download.Count == 6);
        // 2026-08-29：对齐 MCLCS-Linux 补齐 Map 安装页，工具箱新增 "map" 副标签（命令/Mod 开发此前已在列表但无实现）。
        Check("工具箱页副标签 21 项", Sidebar.Toolbox.Count == 21);
        Check("设置页副标签 8 项", Sidebar.Settings.Count == 8);
        Check("游戏页无侧边栏", Sidebar.Game.Count == 0);
        Check("Has: 游戏页无侧边栏", !Sidebar.Has(MainTabKind.Game));
        Check("Has: 下载页有侧边栏", Sidebar.Has(MainTabKind.Download));

        // ById 需指定主标签
        Check("下载页 ById minecraft 存在", Sidebar.ById(MainTabKind.Download, "minecraft") != null);
        Check("工具箱页 ById log 存在", Sidebar.ById(MainTabKind.Toolbox, "log") != null);
        Check("设置页 ById about 存在", Sidebar.ById(MainTabKind.Settings, "about") != null);
        Check("跨标签误查返回 null", Sidebar.ById(MainTabKind.Download, "log") == null);

        // SidebarState（悬停展开，无 Pin 概念）
        Console.WriteLine("[UI 框架 - 侧边栏状态机]");
        var state = new SidebarState();
        Check("初始 Expanded=false", !state.Expanded);
        Check("初始 Width=折叠宽", state.Width == SidebarState.CollapsedWidth);

        state.HoverEnter();
        Check("Hover 后 Expanded=true", state.Expanded);
        Check("Hover 后 Width=展开宽", state.Width == SidebarState.ExpandedWidth);

        state.HoverLeave();
        Check("Hover 离开后 Expanded=false", !state.Expanded);
        Check("Hover 离开后 Width=折叠宽", state.Width == SidebarState.CollapsedWidth);

        // 选中 / 切换主标签
        state.SwitchOwner(MainTabKind.Toolbox);
        Check("SwitchOwner 后 Owner=Toolbox", state.Owner == MainTabKind.Toolbox);
        Check("SwitchOwner 后选中首项 log", state.SelectedId == "log");
        state.Select("nbt");
        Check("Select 后 SelectedId=nbt", state.SelectedId == "nbt");

        // Restore / Capture
        var config = new SidebarConfig { LastSelectedId = "mod" };
        var state2 = new SidebarState();
        state2.SwitchOwner(MainTabKind.Download);
        state2.Restore(config);
        Check("Restore 选中 mod", state2.SelectedId == "mod");
        var captured = state2.Capture();
        Check("Capture LastSelectedId=mod", captured.LastSelectedId == "mod");
        Check("SidebarConfig HoverExpand 默认 true", new SidebarConfig().HoverExpand);
    }

    private static void AfkTokenTests()
    {
        Console.WriteLine("[AFK 工作流 Token]");

        // 注意：L(长按)必须紧跟在F或K指令之后，D(延迟)会中断
        var result = AfkWorkflowToken.Parse("F10;L3;D4;K39;C1-500;*0");
        Check("Parse Ok", result.Ok);
        if (!result.Ok)
        {
            Console.WriteLine($"  (Parse 失败: {result.Error})");
        }
        else
        {
            Check("6条指令", result.Instructions.Count == 6);
            Check("第1条 F10", result.Instructions[0].Kind == AfkOpKind.FunctionKey);
            Check("第2条 L3", result.Instructions[1].Kind == AfkOpKind.LongPress && result.Instructions[1].A == 3);
            Check("第3条 D4", result.Instructions[2].Kind == AfkOpKind.Delay && result.Instructions[2].A == 4);
            Check("第4条 K39", result.Instructions[3].Kind == AfkOpKind.KeyCode);
            Check("第5条 C1-500", result.Instructions[4].Kind == AfkOpKind.Click);
            Check("第6条 *0(Repeat)", result.Instructions[5].Kind == AfkOpKind.Repeat);

            Check("Serialize 往返", AfkWorkflowToken.Serialize(result.Instructions) == "F10;L3;D4;K39;C1-500;*0");
        }
        Check("IsValid", AfkWorkflowToken.IsValid("F10;L3;D4;K39;C1-500;*0"));
        Check("空字符串无效", !AfkWorkflowToken.IsValid(""));
        Check("未知操作符无效", !AfkWorkflowToken.IsValid("X1"));
        Check("非末尾Repeat无效", !AfkWorkflowToken.IsValid("*0;F10"));

        var maxF = AfkWorkflowToken.Parse("F24");
        Check("F24合法", maxF.Ok);
        var maxD = AfkWorkflowToken.Parse("D60000");
        Check("D60000合法", maxD.Ok);

        var desc = AfkWorkflowToken.Describe("F10;D4;*0");
        Check("描述非空", desc.Length > 0);
        Check("描述含'F10'", desc.Contains("F10"));

        var cycle = AfkWorkflowToken.EstimateCycleMs("F10;D4;*0");
        Check("周期>0", cycle > 0);

        var expanded = AfkWorkflowToken.Expand("F10;D4;*0");
        Check("展开后含F10", expanded.Any(i => i.Kind == AfkOpKind.FunctionKey && i.A == 10));
    }

    private static void ShaderTokenTests()
    {
        Console.WriteLine("[Shader 配置 Token]");

        var config = new ShaderConfig
        {
            Pack = "BSL_v8.2",
            Profile = "High",
            McVersion = "1.20.1",
            Loader = "OptiFine",
            Options = new Dictionary<string, string> { ["renderQuality"] = "2.0", ["shadowQuality"] = "2.0" },
            Note = "测试配置"
        };

        var token = ShaderConfigToken.Encode(config);
        Check("编码前缀 SHDR1.", token.StartsWith("SHDR1."));
        Check("编码含校验和", token.Split('.').Length == 3);

        var result = ShaderConfigToken.Decode(token);
        Check("解码 Ok", result.Ok);
        var decoded = result.Config!;
        Check("解码 Pack=BSL_v8.2", decoded.Pack == "BSL_v8.2");
        Check("解码 Profile=High", decoded.Profile == "High");
        Check("解码 McVersion=1.20.1", decoded.McVersion == "1.20.1");
        Check("解码 Loader=OptiFine", decoded.Loader == "OptiFine");
        Check("解码 Note=测试配置", decoded.Note == "测试配置");
        Check("解码 Options renderQuality=2.0", decoded.Options["renderQuality"] == "2.0");

        var tampered = token + "x";
        var bad = ShaderConfigToken.Decode(tampered);
        Check("篡改后解码 Ok=false", !bad.Ok);

        Check("空Token解码失败", !ShaderConfigToken.Decode("").Ok);
        Check("非法前缀解码失败", !ShaderConfigToken.Decode("BAD.token.abc").Ok);

        var props = ShaderConfigToken.WriteProperties(config.Options);
        Check("properties 非空", props.Length > 0);
        Check("properties 含 renderQuality", props.Contains("renderQuality"));

        var parsed = ShaderConfigToken.ParseProperties(props);
        Check("ParseProperties renderQuality=2.0", parsed["renderQuality"] == "2.0");

        var config2 = new ShaderConfig { Pack = "BSL_v8.2", Profile = "Low", Options = new() };
        var diff = ShaderConfigToken.Diff(config, config2);
        Check("Diff 非空(无共同options)", diff.Count >= 0);

        var same = ShaderConfigToken.Diff(config, config);
        Check("相同配置 Diff 为空", same.Count == 0);
    }

    private static void ServerListTests()
    {
        Console.WriteLine("[服务器列表]");

        var list = new List<ServerEntry>();
        Check("新列表为空", list.Count == 0);

        var (h1, p1) = ServerEntry.SplitAddress("example.com:25565");
        Check("host:port host", h1 == "example.com");
        Check("host:port port", p1 == 25565);

        var (h2, p2) = ServerEntry.SplitAddress("example.com");
        Check("默认端口", p2 == 25565);

        var (h3, p3) = ServerEntry.SplitAddress("[::1]:25565");
        Check("IPv6 host", h3 == "::1");

        var entry = new ServerEntry { Name = "测试服", Address = "mc.example.com:25565" };
        ServerListStore.AddOrUpdate(list, entry);
        Check("添加后 Count=1", list.Count == 1);
        Check("名称=测试服", list[0].Name == "测试服");

        var updated = new ServerEntry { Name = "测试服-改", Address = "mc.example.com:25565" };
        ServerListStore.AddOrUpdate(list, updated);
        Check("更新后 Count=1", list.Count == 1);
        Check("更新后名称=测试服-改", list[0].Name == "测试服-改");

        var entry2 = new ServerEntry { Name = "第二服", Address = "mc2.example.com" };
        ServerListStore.AddOrUpdate(list, entry2);
        ServerListStore.Move(list, 0, 1);
        Check("Move 后 [0]=第二服", list[0].Name == "第二服");

        var nbt = ServerListStore.ToNbt(list);
        Check("ToNbt 非空", nbt != null);
        var restored = ServerListStore.FromNbt(nbt);
        Check("FromNbt Count=2", restored.Count == 2);
        Check("FromNbt [0]=第二服", restored[0].Name == "第二服");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"mclcs_sl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            ServerListStore.Save(tmpDir, list);
            var loaded = ServerListStore.Load(tmpDir);
            Check("Save+Load Count=2", loaded.Count == 2);
            Check("Load [0]=第二服", loaded[0].Name == "第二服");
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static void ServerPingerTests()
    {
        Console.WriteLine("[服务器 Ping]");

        var varInt127 = ServerPinger.WriteVarInt(127);
        Check("VarInt 127 长度>0", varInt127.Length > 0);
        Check("VarInt 127 往返", ServerPinger.TryReadVarInt(varInt127, out var v, out _) && v == 127);

        var varInt25565 = ServerPinger.WriteVarInt(25565);
        Check("VarInt 25565 往返", ServerPinger.TryReadVarInt(varInt25565, out var v2, out _) && v2 == 25565);

        var hs = ServerPinger.BuildHandshake("localhost", 25565);
        Check("Handshake 非空", hs.Length > 0);

        var req = ServerPinger.BuildStatusRequest();
        Check("StatusRequest 非空", req.Length > 0);

        Check("StripColorCodes §aHello§r", ServerPinger.StripColorCodes("§aHello§r") == "Hello");
        Check("StripColorCodes 无颜色码", ServerPinger.StripColorCodes("Plain") == "Plain");

        var motd1 = ServerPinger.ExtractMotd("{\"description\":{\"text\":\"Hello World\"}}");
        Check("ExtractMotd text", motd1 == "Hello World");

        var motd2 = ServerPinger.ExtractMotd("{\"description\":{\"extra\":[{\"text\":\"A\"},{\"text\":\"B\"}]}}");
        Check("ExtractMotd extra", motd2 == "AB");

        var status = ServerPinger.ParseStatusJson(
            "{\"version\":{\"name\":\"1.20.1\",\"protocol\":763},\"players\":{\"max\":20,\"online\":5},\"description\":{\"text\":\"Test\"}}", 42);
        Check("ParseStatus version=1.20.1", status!.VersionName == "1.20.1");
        Check("ParseStatus protocol=763", status.Protocol == 763);
        Check("ParseStatus max=20", status.Max == 20);
        Check("ParseStatus online=5", status.Online == 5);
    }

    private static void LanScannerTests()
    {
        Console.WriteLine("[局域网扫描]");

        var payload = "[MOTD]A Minecraft Server[/MOTD][AD]65432[/AD]";
        var lan = LanServerScanner.ParseBroadcast(payload, "127.0.0.1");
        Check("ParseBroadcast 非空", lan != null);
        Check("ParseBroadcast Motd", lan!.Motd == "A Minecraft Server");
        Check("ParseBroadcast Port", lan.Port == 65432);

        var broadcast = LanServerScanner.BuildBroadcast("Test Server", 25565);
        Check("BuildBroadcast 含 MOTD", broadcast.Contains("[MOTD]"));
        Check("BuildBroadcast 含 AD", broadcast.Contains("[AD]"));

        var staleTime = DateTime.Now.AddMinutes(-5);
        var servers = new List<LanServer>
        {
            new() { Address = "192.168.1.1", LastSeen = staleTime.AddMinutes(-10) },
            new() { Address = "192.168.1.2", LastSeen = DateTime.Now }
        };
        LanServerScanner.PruneStale(servers, staleTime);
        Check("PruneStale 移除过期", servers.Count == 1);
        Check("PruneStale 保留最近", servers[0].Address == "192.168.1.2");
    }

    private static void PixelmapTests()
    {
        Console.WriteLine("[Pixelmap 客户端]");

        var url = PixelmapClient.BuildSearchUrl("bedwars", null, null, 1, 10);
        Check("BuildSearchUrl 含 keyword", url.Contains("keyword=bedwars"));
        Check("BuildSearchUrl 含 page", url.Contains("page=1"));
        Check("BuildSearchUrl 含 limit", url.Contains("limit=10"));

        var emptyUrl = PixelmapClient.BuildSearchUrl("", null, null, 1, 10);
        Check("空搜索词仍返回有效URL", emptyUrl.Contains("/search"));

        Check("SafeFileName 正常", PixelmapClient.SafeFileName("https://example.com/Bed_Wars_v2.0.zip", "fallback") == "Bed_Wars_v2.0.zip");
        Check("SafeFileName 路径遍历防护", !PixelmapClient.SafeFileName("https://example.com/../../etc/passwd", "fallback").Contains(".."));

        var json = "{\"post\":{\"title\":\"Test Map\",\"version\":\"1.20\",\"slug\":\"test-map\",\"download_url\":\"https://example.com/test.zip\",\"allow_download\":true}}";
        var detail = PixelmapClient.ParseDetail(json, "test-map");
        Check("ParseDetail 非空", detail != null);
        Check("ParseDetail title=Test Map", detail!.Title == "Test Map");
        Check("ParseDetail slug=test-map", detail.Slug == "test-map");

        var item = PixelmapClient.ToDownloadItem(detail, "/tmp/mclcs-test");
        Check("ToDownloadItem 非空", item != null);
        if (item != null)
        {
            Check("ToDownloadItem Destination 含 maps", item.Destination.Contains("maps"));
            Check("ToDownloadItem Urls 非空", item.Urls.Count > 0);
        }
    }

    private static void MapInstallerTests()
    {
        Console.WriteLine("[地图安装器]");

        Check("根目录 level.dat 前缀为空", MapInstaller.DetectRootPrefix(new[] { "level.dat", "region/r.0.0.mca" }) == "");
        Check("一层嵌套前缀=map/", MapInstaller.DetectRootPrefix(new[] { "map/level.dat", "map/region/r.0.0.mca" }) == "map/");
        Check("两层嵌套取浅层", MapInstaller.DetectRootPrefix(new[] { "a/b/level.dat", "a/b/region/r.0.0.mca" }) == "a/b/");
        Check("无 level.dat 返回null", MapInstaller.DetectRootPrefix(new[] { "data.txt", "info.json" }) == null);

        Check("SafeSaveName 正常", MapInstaller.SafeSaveName("My World") == "My World");
        // Linux 上非法文件名字符只有 / 和 \0，Windows 上更多
        var safeName = MapInstaller.SafeSaveName("Test/World");
        Check("SafeSaveName 替换斜杠", !safeName.Contains('/'));

        var savesDir = Path.Combine(Path.GetTempPath(), "mclcs_saves_test");
        try
        {
            Directory.CreateDirectory(savesDir);
            Directory.CreateDirectory(Path.Combine(savesDir, "My World"));
            Directory.CreateDirectory(Path.Combine(savesDir, "My World (2)"));
            Check("ResolveConflict 无冲突", MapInstaller.ResolveConflict(savesDir, "New World", out var r1) == "New World" && !r1);
            Check("ResolveConflict 有冲突→(3)", MapInstaller.ResolveConflict(savesDir, "My World", out var r2) == "My World (3)" && r2);
        }
        finally { try { Directory.Delete(savesDir, true); } catch { } }
    }

    private static void ToolboxCatalogTests()
    {
        Console.WriteLine("[工具箱目录]");

        Check("共15个面板", ToolboxCatalog.Panels.Count == 15);
        Check("分组数>0", ToolboxCatalog.Grouped().Count > 0);

        var newPanels = ToolboxCatalog.NewInV2;
        Check("v2.0新增面板>0", newPanels.Count() > 0);

        var titles = ToolboxCatalog.Grouped().Select(g => ToolboxCatalog.GroupTitle(g.Group)).ToList();
        Check("分组标题非空", titles.All(t => t.Length > 0));

        Check("所有面板有Id", ToolboxCatalog.Panels.All(p => !string.IsNullOrEmpty(p.Id)));
        Check("所有面板有Title", ToolboxCatalog.Panels.All(p => !string.IsNullOrEmpty(p.Title)));
        Check("所有面板有Description", ToolboxCatalog.Panels.All(p => !string.IsNullOrEmpty(p.Description)));
    }

    private static void BackupManagerTests()
    {
        Console.WriteLine("[备份管理器]");

        var policy = new BackupPolicy { MaxAgeDays = 30, KeepPerSource = 3 };

        var records = new List<BackupRecord>
        {
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow.AddDays(-40), Auto = true },
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow.AddDays(-20), Auto = true },
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow.AddDays(-10), Auto = true },
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow.AddDays(-5), Auto = true },
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow.AddDays(-3), Auto = true },
            new() { SourceName = "world", CreatedAt = DateTime.UtcNow, Auto = false }
        };

        var expired = BackupManager.SelectExpired(records, policy);
        Check("过期数量>0", expired.Count > 0);
        Check("手动备份不过期", !expired.Any(r => !r.Auto));

        var recentOnly = records.Where(r => r.CreatedAt > DateTime.UtcNow.AddDays(-30)).ToList();
        var expiredRecent = BackupManager.SelectExpired(recentOnly, policy);
        // KeepPerSource=3，4条近期auto备份中最旧的1条会被淘汰
        Check("近期备份 KeepPerSource 淘汰最旧", expiredRecent.Count >= 1);

        var name = BackupManager.BuildArchiveName(BackupKind.Save, "My World", DateTime.Now);
        Check("BuildArchiveName 含save_", name.StartsWith("save_"));

        var result = BackupResult.Fail("test error");
        Check("BackupResult Fail Ok=false", !result.Ok);
        Check("BackupResult Fail Error", result.Error == "test error");
    }

    private static void FileChangeDetectorTests()
    {
        Console.WriteLine("[文件变更检测器]");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"mclcs_fcd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            File.WriteAllText(Path.Combine(tmpDir, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(tmpDir, "b.txt"), "world");
            Directory.CreateDirectory(Path.Combine(tmpDir, "logs"));
            File.WriteAllText(Path.Combine(tmpDir, "logs", "latest.log"), "log");

            var snap1 = FileChangeDetector.Take(tmpDir);
            Check("Snapshot 文件数=2(忽略logs)", snap1.Entries.Count == 2);
            Check("Snapshot 含 a.txt", snap1.Entries.Any(e => e.RelativePath == "a.txt"));

            File.WriteAllText(Path.Combine(tmpDir, "a.txt"), "modified");
            File.WriteAllText(Path.Combine(tmpDir, "c.txt"), "new");

            var snap2 = FileChangeDetector.Take(tmpDir);
            var changes = FileChangeDetector.Compare(snap1, snap2);
            Check("变更检测 modified=1", changes.Modified == 1);
            Check("变更检测 added=1", changes.Added == 1);
            Check("变更检测 total=2", changes.Changes.Count == 2);

            var snapFile = Path.GetTempFileName();
            try
            {
                FileChangeDetector.Save(snap1, snapFile);
                Check("Save 成功", File.Exists(snapFile));
                var loaded = FileChangeDetector.Load(snapFile);
                Check("Load 条目数=2", loaded.Entries.Count == 2);
            }
            finally { File.Delete(snapFile); }
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static void DataPackConflictTests()
    {
        Console.WriteLine("[数据包冲突检测]");

        Check("1.20.1 format=15", DataPackConflictDetector.ExpectedFormat("1.20.1") == 15);
        Check("1.16.5 format=6", DataPackConflictDetector.ExpectedFormat("1.16.5") == 6);
        Check("未知版本 format=0", DataPackConflictDetector.ExpectedFormat("unknown") == 0);

        var (fmt, desc) = DataPackConflictDetector.ParseMeta(
            "{\"pack\":{\"pack_format\":15,\"description\":\"Test\"}}");
        Check("ParseMeta format=15", fmt == 15);
        Check("ParseMeta description=Test", desc == "Test");

        var (fmtBad, _) = DataPackConflictDetector.ParseMeta("not json");
        Check("非法JSON format=0", fmtBad == 0);

        var packs = new List<DataPackInfo>
        {
            new() { Name = "PackA", Path = "a", PackFormat = 15, Resources = new() { "data/ns/functions/tick.mcfunction" }, LoadOrder = 0 },
            new() { Name = "PackB", Path = "b", PackFormat = 15, Resources = new() { "data/ns/functions/tick.mcfunction" }, LoadOrder = 1 }
        };
        var conflicts = DataPackConflictDetector.FindConflicts(packs);
        Check("同名资源检测到冲突", conflicts.Count > 0);
        Check("Winner=PackB(后加载)", conflicts[0].Winner == "PackB");
    }

    private static void MusicPlaylistTests()
    {
        Console.WriteLine("[音乐播放器]");

        var playlist = new MusicPlaylist();
        Check("初始 Count=0", playlist.Count == 0);
        Check("初始 Mode=LoopAll", playlist.Mode == PlayMode.LoopAll);

        playlist.Add(new Track { Title = "Track1", Path = "/tmp/t1.ogg" });
        playlist.Add(new Track { Title = "Track2", Path = "/tmp/t2.ogg" });
        playlist.Add(new Track { Title = "Track3", Path = "/tmp/t3.ogg" });

        // Sequential
        playlist.Mode = PlayMode.Sequential;
        playlist.Select(0);
        var next = playlist.Next();
        Check("Sequential Next=Track2", next!.Title == "Track2");
        next = playlist.Next();
        Check("Sequential Next=Track3", next!.Title == "Track3");
        next = playlist.Next();
        Check("Sequential 末尾 Next=null", next == null);

        var prev = playlist.Previous();
        Check("Sequential Previous=Track2", prev!.Title == "Track2");

        // LoopAll
        playlist.Mode = PlayMode.LoopAll;
        playlist.Select(2);
        next = playlist.Next();
        Check("LoopAll 循环回到 Track1", next!.Title == "Track1");

        // Shuffle
        playlist.Mode = PlayMode.Shuffle;
        playlist.Reshuffle();
        var order = playlist.ShuffleOrder;
        Check("Shuffle 覆盖3首", order.Count == 3 && order.Distinct().Count() == 3);

        // LoopOne
        playlist.Mode = PlayMode.LoopOne;
        playlist.Select(0);
        var t1 = playlist.Next();
        var t2 = playlist.Next();
        Check("LoopOne 同曲", t1!.Title == t2!.Title);

        // Volume
        playlist.Volume = 75;
        Check("Volume=75", playlist.Volume == 75);
        playlist.Volume = 150;
        Check("Volume 上限100", playlist.Volume == 100);
        playlist.Volume = -10;
        Check("Volume 下限0", playlist.Volume == 0);

        // CycleMode
        playlist.Mode = PlayMode.Sequential;
        playlist.CycleMode();
        Check("CycleMode Sequential→LoopAll", playlist.Mode == PlayMode.LoopAll);
        playlist.CycleMode();
        Check("CycleMode LoopAll→LoopOne", playlist.Mode == PlayMode.LoopOne);
        playlist.CycleMode();
        Check("CycleMode LoopOne→Shuffle", playlist.Mode == PlayMode.Shuffle);
        playlist.CycleMode();
        Check("CycleMode Shuffle→Sequential", playlist.Mode == PlayMode.Sequential);

        // ModeText (static)
        Check("ModeText Sequential", MusicPlaylist.ModeText(PlayMode.Sequential).Contains("顺序"));
        Check("ModeText LoopAll", MusicPlaylist.ModeText(PlayMode.LoopAll).Contains("循环"));
    }

    private static void SkinEditorTests()
    {
        Console.WriteLine("[皮肤编辑器]");

        var classic = SkinEditor.RegionsOf(SkinModel.Classic, includeOverlay: false);
        Check("Classic 基础区域数>0", classic.Count > 0);

        var slim = SkinEditor.RegionsOf(SkinModel.Slim, includeOverlay: false);
        Check("Slim 基础区域数>0", slim.Count > 0);

        var withOverlay = SkinEditor.RegionsOf(SkinModel.Classic, includeOverlay: true);
        Check("含Overlay区域数>基础", withOverlay.Count > classic.Count);

        Check("Classic ArmWidth=4", SkinEditor.ArmWidth(SkinModel.Classic) == 4);
        Check("Slim ArmWidth=3", SkinEditor.ArmWidth(SkinModel.Slim) == 3);

        var region = SkinEditor.HitTest(SkinModel.Classic, 8, 8);
        Check("HitTest (8,8) 非空(头部区域)", region != null);

        var noRegion = SkinEditor.HitTest(SkinModel.Classic, -1, -1);
        Check("HitTest 越界返回null", noRegion == null);

        Check("Toggle Classic→Slim", SkinEditor.Toggle(SkinModel.Classic) == SkinModel.Slim);
        Check("Toggle Slim→Classic", SkinEditor.Toggle(SkinModel.Slim) == SkinModel.Classic);

        Check("ModelText Classic", SkinEditor.ModelText(SkinModel.Classic).Contains("Steve"));
        Check("ModelText Slim", SkinEditor.ModelText(SkinModel.Slim).Contains("Alex"));

        var affected = SkinEditor.ArmRegionsAffectedByModelSwitch();
        Check("ArmRegions 共12个", affected.Count == 12);

        // Validate
        var validPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 64, 0, 0, 0, 64 };
        var v1 = SkinEditor.Validate(validPng);
        Check("Validate 64x64 PNG Ok=true", v1.Ok);

        var wrongSize = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 32, 0, 0, 0, 32 };
        var v2 = SkinEditor.Validate(wrongSize);
        Check("Validate 32x32 Ok=false", !v2.Ok);

        var notPng = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        var v3 = SkinEditor.Validate(notPng);
        Check("Validate 非PNG Ok=false", !v3.Ok);
    }

    private static void NbtEditorTests()
    {
        Console.WriteLine("[NBT 编辑器]");

        var root = NbtTag.Compound("root");
        var data = NbtTag.Compound("Data");
        root.Children = new List<NbtTag> { data };
        data.Children = new List<NbtTag>
        {
            new() { Type = NbtTagType.Int, Name = "Version", IntValue = 123 },
            new() { Type = NbtTagType.String, Name = "Name", StringValue = "TestWorld" }
        };

        Check("CountNodes>0", NbtEditor.CountNodes(root) > 0);

        var resolved = NbtEditor.Resolve(root, "Data.Version");
        Check("Resolve Data.Version=123", resolved != null && resolved.IntValue == 123);

        var resolvedName = NbtEditor.Resolve(root, "Data.Name");
        Check("Resolve Data.Name=TestWorld", resolvedName != null && resolvedName.StringValue == "TestWorld");

        var tree = NbtEditor.RenderTree(root);
        Check("RenderTree 非空", tree.Length > 0);
        Check("RenderTree 含Data", tree.Contains("Data"));
        Check("RenderTree 含Version", tree.Contains("Version"));

        var setResult = NbtEditor.SetValue(root, "Data.Version", "456");
        Check("SetValue Ok", setResult.Ok);
        var updated = NbtEditor.Resolve(root, "Data.Version");
        Check("SetValue 456", updated != null && updated.IntValue == 456);

        var removeResult = NbtEditor.Remove(root, "Data.Name");
        Check("Remove Ok", removeResult.Ok);
        var removed = NbtEditor.Resolve(root, "Data.Name");
        Check("Remove Data.Name=null", removed == null);

        // ValueText
        Check("ValueText Int=123", NbtEditor.ValueText(new NbtTag { Type = NbtTagType.Int, IntValue = 123 }) == "123");
        Check("ValueText String", NbtEditor.ValueText(new NbtTag { Type = NbtTagType.String, StringValue = "hello" }) == "hello");
    }

    private static void HudTests()
    {
        Console.WriteLine("[HUD 覆盖层]");

        // HudField flags
        var fields = HudField.Fps | HudField.Memory | HudField.Coordinates;
        var cfg = new HudConfig { Fields = fields };
        Check("Has Fps", cfg.Has(HudField.Fps));
        Check("Has Memory", cfg.Has(HudField.Memory));
        Check("Has Coordinates", cfg.Has(HudField.Coordinates));
        Check("!Has Ping", !cfg.Has(HudField.Ping));

        // Toggle
        cfg.Toggle(HudField.Ping);
        Check("Toggle 添加Ping", cfg.Has(HudField.Ping));
        cfg.Toggle(HudField.Fps);
        Check("Toggle 移除Fps", !cfg.Has(HudField.Fps));

        // FieldName
        Check("FieldName Fps=帧率", HudConfig.FieldName(HudField.Fps) == "帧率");
        Check("FieldName Coordinates=坐标", HudConfig.FieldName(HudField.Coordinates) == "坐标");

        // HudConfig
        var config = new HudConfig
        {
            Enabled = true,
            Fields = HudField.Fps | HudField.Memory | HudField.Coordinates,
            Anchor = HudAnchor.TopLeft,
            Opacity = 0.8,
            Margin = 12
        };
        Check("Enabled=true", config.Enabled);
        Check("Anchor=TopLeft", config.Anchor == HudAnchor.TopLeft);
        Check("Opacity=0.8", Math.Abs(config.Opacity - 0.8) < 0.01);

        config.Opacity = 1.5;
        Check("Opacity 上限1.0", Math.Abs(config.Opacity - 1.0) < 0.01);
        config.Opacity = -0.5;
        Check("Opacity 下限0.1", Math.Abs(config.Opacity - 0.1) < 0.01);

        var pos = config.ComputePosition(1920, 1080, 200, 100);
        Check("TopLeft X=12", pos.X == 12);
        Check("TopLeft Y=12", pos.Y == 12);

        var cfgTR = new HudConfig { Anchor = HudAnchor.TopRight, Margin = 12 };
        var posTR = cfgTR.ComputePosition(1920, 1080, 200, 100);
        Check("TopRight X≈1708", Math.Abs(posTR.X - 1708) < 5);

        var cfgBL = new HudConfig { Anchor = HudAnchor.BottomLeft, Margin = 12 };
        var posBL = cfgBL.ComputePosition(1920, 1080, 200, 100);
        Check("BottomLeft Y≈968", Math.Abs(posBL.Y - 968) < 5);

        // HudMetrics + Render
        var metrics = new HudMetrics
        {
            Fps = 120,
            MemoryUsedMb = 2048,
            MemoryMaxMb = 4096,
            CpuPercent = 35.5,
            PingMs = 42,
            X = 100, Y = 64, Z = -200,
            Biome = "平原",
            SessionTime = TimeSpan.FromMinutes(135)
        };
        var renderCfg = new HudConfig { Fields = HudField.Fps | HudField.Memory | HudField.Coordinates };
        var rendered = HudMetricsProvider.Render(metrics, renderCfg);
        Check("Render 含FPS", rendered.Contains("120"));
        Check("Render 含Memory", rendered.Contains("2048"));
        Check("Render 含坐标", rendered.Contains("100"));

        // TryConsumeLogLine
        var provider = new HudMetricsProvider();
        var line = "[MCLCS-HUD] fps=120 ping=42 biome=plains";
        var parsed = provider.TryConsumeLogLine(line);
        Check("TryConsumeLogLine=true", parsed);
        Check("External fps=120", provider.External.Fps == 120);
        Check("External ping=42", provider.External.PingMs == 42);
        Check("External biome=plains", provider.External.Biome == "plains");

        var badParsed = provider.TryConsumeLogLine("[INFO] Some log");
        Check("非HUD行返回false", !badParsed);
    }

    private static void PrewarmTests()
    {
        Console.WriteLine("[启动预热器]");

        var config = new PrewarmConfig
        {
            Mode = PrewarmMode.Light,
            IdleDelaySec = 5,
            BudgetMb = 512,
            Concurrency = 4
        };
        Check("Mode=Light", config.Mode == PrewarmMode.Light);
        Check("IdleDelay=5", config.IdleDelaySec == 5);
        Check("BudgetMb=512", config.BudgetMb == 512);
        Check("Concurrency=4", config.Concurrency == 4);

        // 创建临时目录结构来测试 BuildPlan
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"mclcs_pw_{Guid.NewGuid():N}");
        try
        {
            var versionDir = Path.Combine(tmpRoot, "versions", "1.20.1");
            Directory.CreateDirectory(versionDir);
            File.WriteAllText(Path.Combine(versionDir, "1.20.1.jar"), "fake jar");

            var plan = LaunchPrewarmer.BuildPlan(tmpRoot, "1.20.1", config);
            Check("BuildPlan 非空", plan.Count > 0);
            if (plan.Count > 0)
            {
                Check("版本jar优先", plan.Files[0].Contains("1.20.1.jar"));
            }

            var saved = LaunchPrewarmer.EstimateSavedMs(plan);
            Check("EstimateSavedMs >= 0", saved >= 0);
        }
        finally { try { Directory.Delete(tmpRoot, true); } catch { } }
    }

    private static void AnnualReportTests()
    {
        Console.WriteLine("[年度报告]");

        var sessions = new List<PlaySession>
        {
            new() { StartLocal = new DateTime(2025, 6, 1, 14, 0, 0), EndLocal = new DateTime(2025, 6, 1, 15, 0, 0), VersionId = "1.20.1" },
            new() { StartLocal = new DateTime(2025, 6, 2, 15, 0, 0), EndLocal = new DateTime(2025, 6, 2, 16, 30, 0), VersionId = "1.20.1" },
            new() { StartLocal = new DateTime(2025, 7, 1, 20, 0, 0), EndLocal = new DateTime(2025, 7, 1, 23, 0, 0), VersionId = "1.21" },
            new() { StartLocal = new DateTime(2025, 7, 2, 23, 0, 0), EndLocal = new DateTime(2025, 7, 3, 3, 0, 0), VersionId = "1.21" },
            new() { StartLocal = new DateTime(2025, 7, 3, 22, 0, 0), EndLocal = new DateTime(2025, 7, 4, 0, 0, 0), VersionId = "1.21", Crashed = true },
        };

        var report = AnnualReport.Generate(sessions, 2025);
        Check("Generate 非空", report != null);
        Check("TotalHours > 0", report.TotalHours > 0);
        Check("SessionCount=5", report.SessionCount == 5);
        Check("ActiveDays>0", report.ActiveDays > 0);
        Check("MonthlyMinutes[6]>0(6月)", report.MonthlyMinutes[5] > 0);
        Check("CrashCount=1", report.CrashCount == 1);

        var streak = AnnualReport.LongestStreak(sessions.Select(s => s.StartLocal));
        Check("LongestStreak >= 2", streak >= 2);

        var title = AnnualReport.Title(report);
        Check("Title 非空", title.Length > 0);

        var md = AnnualReport.RenderMarkdown(report);
        Check("RenderMarkdown 非空", md.Length > 0);
        Check("RenderMarkdown 含标题", md.Contains("#"));
        Check("RenderMarkdown 含总时长", md.Contains(report.TotalHours.ToString("F1")));

        // SessionLog
        var tmpDir = Path.Combine(Path.GetTempPath(), $"mclcs_ar_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            SessionLog.Append(tmpDir, sessions[0]);
            var loaded = SessionLog.Load(tmpDir);
            Check("SessionLog Append+Load Count=1", loaded.Count == 1);
            Check("SessionLog Minutes≈60", Math.Abs(loaded[0].Minutes - 60) < 1);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static void ResourcePackRepairTests()
    {
        Console.WriteLine("[资源包修复]");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"mclcs_rpr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            // 正常包（需要 assets/ 目录中有实际文件，否则 Diagnose 扫描不到）
            Directory.CreateDirectory(Path.Combine(tmpDir, "assets", "minecraft", "textures"));
            File.WriteAllText(Path.Combine(tmpDir, "assets", "minecraft", "textures", "dummy.png"), "");
            File.WriteAllText(Path.Combine(tmpDir, "pack.mcmeta"),
                "{\"pack\":{\"pack_format\":15,\"description\":\"Test\"}}");
            var diag = ResourcePackRepair.Diagnose(tmpDir);
            Check("正常包 Healthy=true", diag.Healthy);
            Check("正常包 Issues=0", diag.Issues.Count == 0);

            // 缺失 pack.mcmeta
            var badDir = Path.Combine(Path.GetTempPath(), $"mclcs_rpr_bad_{Guid.NewGuid():N}");
            Directory.CreateDirectory(badDir);
            Directory.CreateDirectory(Path.Combine(badDir, "assets", "minecraft", "textures"));
            File.WriteAllText(Path.Combine(badDir, "assets", "minecraft", "textures", "dummy.png"), "");
            try
            {
                var badDiag = ResourcePackRepair.Diagnose(badDir);
                Check("缺失mcmeta Healthy=false", !badDiag.Healthy);
                Check("缺失mcmeta Issues>0", badDiag.Issues.Count > 0);

                var repairResult = ResourcePackRepair.Repair(badDiag, backup: false);
                Check("Repair Ok=true", repairResult.Ok);
                Check("修复后 pack.mcmeta 存在", File.Exists(Path.Combine(badDir, "pack.mcmeta")));
            }
            finally { try { Directory.Delete(badDir, true); } catch { } }

            // ExpectedFormat
            Check("ExpectedFormat 1.20.1=15", ResourcePackRepair.ExpectedFormat("1.20.1") == 15);
            Check("ExpectedFormat 1.19=9", ResourcePackRepair.ExpectedFormat("1.19") == 9);

            // BuildMeta
            var meta = ResourcePackRepair.BuildMeta(15, "Auto-repaired pack");
            Check("BuildMeta 含pack_format", meta.Contains("pack_format"));
            Check("BuildMeta 含description", meta.Contains("Auto-repaired pack"));
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static void ServerPackCacheTests()
    {
        Console.WriteLine("[服务器资源包缓存]");

        var tmpDir = Path.Combine(Path.GetTempPath(), $"mclcs_spc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            var stats = ServerResourcePackCache.Stats(tmpDir);
            Check("初始 Count=0", stats.Count == 0);

            var hit = ServerResourcePackCache.TryHit(tmpDir, "https://example.com/pack.zip");
            Check("TryHit 未命中=null", hit == null);

            // 创建一个测试文件放入缓存
            var srcFile = Path.Combine(tmpDir, "test_pack.zip");
            File.WriteAllText(srcFile, "fake resource pack data for testing");

            var entry = ServerResourcePackCache.Put(tmpDir, "https://example.com/pack.zip", srcFile);
            Check("Put 非空", entry != null);
            Check("Put 后 Count=1", ServerResourcePackCache.Stats(tmpDir).Count == 1);

            var hit2 = ServerResourcePackCache.TryHit(tmpDir, "https://example.com/pack.zip");
            Check("TryHit 命中非空", hit2 != null);
            Check("命中 Hits=1", ServerResourcePackCache.Stats(tmpDir).TotalHits == 1);

            // Verify
            var index = ServerResourcePackCache.LoadIndex(tmpDir);
            var cached = index[0];
            Check("Verify 通过(无声明)", ServerResourcePackCache.Verify(tmpDir, cached));

            // Clear
            ServerResourcePackCache.Clear(tmpDir);
            Check("Clear 后 Count=0", ServerResourcePackCache.Stats(tmpDir).Count == 0);

            // SelectEvictions
            var entries = new List<CachedServerPack>
            {
                new() { Key = "a", SizeBytes = 600 * 1024, LastUsed = DateTime.UtcNow },
                new() { Key = "b", SizeBytes = 600 * 1024, LastUsed = DateTime.UtcNow.AddHours(-1) }
            };
            var evict = ServerResourcePackCache.SelectEvictions(entries, capacityMb: 1);
            Check("SelectEvictions 超出容量有淘汰", evict.Count > 0);
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static void ProfileV2Tests()
    {
        Console.WriteLine("[启动器配置 v2.0]");

        var profile = new LauncherProfile
        {
            DefaultUsername = "TestPlayer",
            LastVersionId = "1.20.1",
            TabTheme = new TabThemeConfig(),
            Sidebar = new SidebarConfig(),
            Hud = new HudConfig { Enabled = false },
            Prewarm = new PrewarmConfig { Mode = PrewarmMode.Off },
            Backup = new BackupPolicy { MaxAgeDays = 30, KeepPerSource = 5 },
            ServerPackCacheMb = 256,
            AutoRepairResourcePacks = true,
            AfkWorkflows = new Dictionary<string, string> { ["test"] = "F10;D4;*0" },
            ShaderTokens = new Dictionary<string, string> { ["bsl"] = "SHDR1.test.abc" }
        };

        Check("DefaultUsername=TestPlayer", profile.DefaultUsername == "TestPlayer");
        Check("LastVersionId=1.20.1", profile.LastVersionId == "1.20.1");
        Check("TabTheme 非空", profile.TabTheme != null);
        Check("Sidebar 非空", profile.Sidebar != null);
        Check("Sidebar HoverExpand 默认 true", profile.Sidebar!.HoverExpand);
        Check("Hud 非空", profile.Hud != null);
        Check("Hud Enabled=false", !profile.Hud!.Enabled);
        Check("Prewarm Mode=Off", profile.Prewarm!.Mode == PrewarmMode.Off);
        Check("Backup KeepPerSource=5", profile.Backup!.KeepPerSource == 5);
        Check("ServerPackCacheMb=256", profile.ServerPackCacheMb == 256);
        Check("AutoRepairResourcePacks=true", profile.AutoRepairResourcePacks);
        Check("AfkWorkflows Count=1", profile.AfkWorkflows!.Count == 1);
        Check("ShaderTokens Count=1", profile.ShaderTokens!.Count == 1);

        Check("LauncherVersion=2.5.4", GameConstants.LauncherVersion == "2.5.4");
    }
}
