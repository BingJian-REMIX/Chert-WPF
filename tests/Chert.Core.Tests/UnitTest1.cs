using System.Text.Json;
using Chert.Core.Auth;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;
using System.Runtime.InteropServices;
using Xunit;

namespace Chert.Core.Tests;

public class CoreTests
{
    [Fact]
    public void JavaDetector_ParsesMajorVersion()
    {
        Assert.Equal(8, JavaDetector.MajorFromVersionString("1.8.0_301"));
        Assert.Equal(21, JavaDetector.MajorFromVersionString("21.0.3"));
        Assert.Equal(17, JavaDetector.MajorFromVersionString("17"));
    }

    [Fact]
    public void MavenCoordinate_ParseAndLocalPath()
    {
        var c = MavenCoordinate.Parse("org.lwjgl:lwjgl:3.3.1");
        Assert.Equal("org.lwjgl", c.Group);
        Assert.Equal("org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar", c.LocalPath());

        var c2 = MavenCoordinate.Parse("com.example:mod:1.0:natives-linux");
        Assert.Equal("com/example/mod/1.0/mod-1.0-natives-linux.jar", c2.LocalPath());
    }

    [Fact]
    public void ArgumentProcessor_FiltersRulesAndSubstitutes()
    {
        var version = new VersionJson
        {
            Id = "test",
            MainClass = "net.minecraft.client.main.Main",
            Arguments = new Arguments
            {
                Jvm = new List<ArgumentItem>
                {
                    new() { Values = new() { "-XX:+UseG1GC" } },
                    new() { Rules = new() { new Rule { Action = "allow", Os = new OsRule { Name = "linux" } } },
                            Values = new() { "-DLINUX_MARKER=1" } },
                    new() { Rules = new() { new Rule { Action = "allow", Os = new OsRule { Name = "windows" } } },
                            Values = new() { "-DWINDOWS_MARKER=1" } }
                },
                Game = new List<ArgumentItem>
                {
                    new() { Values = new() { "--username", "${auth_player_name}" } }
                }
            }
        };

        var vars = new Dictionary<string, string> { ["auth_player_name"] = "Steve", ["natives_directory"] = "/tmp/n" };
        var resolved = ArgumentProcessor.Process(version, vars, new LaunchOptions { MaxMemoryMb = 4096 }, "/tmp/n");

        Assert.Contains("-XX:+UseG1GC", resolved.JvmArgs);
        // 平台守卫：OS 规则仅放行当前平台的标记（Linux 注入 -DLINUX_MARKER，Windows 注入 -DWINDOWS_MARKER）。
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Contains("-DLINUX_MARKER=1", resolved.JvmArgs);
            Assert.DoesNotContain("-DWINDOWS_MARKER=1", resolved.JvmArgs);
        }
        else
        {
            Assert.Contains("-DWINDOWS_MARKER=1", resolved.JvmArgs);
            Assert.DoesNotContain("-DLINUX_MARKER=1", resolved.JvmArgs);
        }
        Assert.Contains("-Xmx4096M", resolved.JvmArgs);
        Assert.Contains("Steve", resolved.GameArgs);
    }

    [Fact]
    public void ArgumentProcessor_LegacyMinecraftArguments()
    {
        var version = new VersionJson
        {
            Id = "legacy",
            MinecraftArguments = "-Xmx2G -cp ${classpath} net.minecraft.client.main.Main --username ${auth_player_name}"
        };
        var vars = new Dictionary<string, string> { ["auth_player_name"] = "Alex", ["classpath"] = "a.jar" };
        var resolved = ArgumentProcessor.Process(version, vars, new LaunchOptions { MaxMemoryMb = 2048 }, "/tmp/n");

        Assert.Equal("net.minecraft.client.main.Main", resolved.MainClass);
        Assert.Contains("Alex", resolved.GameArgs);
        Assert.Contains("-Xmx2048M", resolved.JvmArgs);
    }

    [Fact]
    public void ClasspathBuilder_ComputesClasspathAndNatives()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_xunit_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vdir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(vdir);
            File.WriteAllText(Path.Combine(vdir, "1.20.1.jar"), "x");

            var libDir = Path.Combine(root, "libraries", "com", "mojang", "log4j", "2.19.1");
            Directory.CreateDirectory(libDir);
            File.WriteAllText(Path.Combine(libDir, "log4j-2.19.1.jar"), "x");

            var version = new VersionJson
            {
                Id = "1.20.1",
                Libraries = new List<Library>
                {
                    new() { Name = "com.mojang:log4j:2.19.1",
                            Downloads = new LibraryDownloads { Artifact = new DownloadInfo { Path = "com/mojang/log4j/2.19.1/log4j-2.19.1.jar" } } },
                    new() { Name = "org.lwjgl:lwjgl:3.3.1",
                            Natives = new Dictionary<string, string> { { "linux", "natives-linux" }, { "windows", "natives-windows" } },
                            Downloads = new LibraryDownloads { Classifiers = new() { ["natives-linux"] = new DownloadInfo { Path = "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar" }, ["natives-windows"] = new DownloadInfo { Path = "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-windows.jar" } } } }
                }
            };

            var cp = ClasspathBuilder.ComputeClasspath(root, "1.20.1", version);
            Assert.Contains("1.20.1.jar", cp);
            Assert.Contains("log4j-2.19.1.jar", cp);

            var natives = ClasspathBuilder.GetNativeEntries(root, version, Path.Combine(root, "natives"));
            Assert.Single(natives);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CrashAnalyzer_DetectsExceptions()
    {
        Assert.Contains("内存不足", CrashAnalyzer.Analyze("java.lang.OutOfMemoryError: Java heap space").ExceptionType);
        var uvc = CrashAnalyzer.Analyze("java.lang.UnsupportedClassVersionError: class file version 65.0");
        Assert.Contains("Java 版本不匹配", uvc.ExceptionType);
        Assert.Contains("Java 21", uvc.Summary);
        Assert.Contains("类未找到", CrashAnalyzer.Analyze("java.lang.ClassNotFoundException: foo.Bar").ExceptionType);
    }

    [Fact]
    public void OfflineAuthenticator_GeneratesStableUuid()
    {
        var uuid = OfflineAuthenticator.GenerateOfflineUuid("Notch");
        Assert.Equal(36, uuid.Length);
        Assert.Equal(uuid, OfflineAuthenticator.GenerateOfflineUuid("Notch"));
    }

    [Fact]
    public async Task VersionMerger_MergesFabricOverVanilla()
    {
        var root = Path.Combine(Path.GetTempPath(), "mclcs_xmerge_" + Guid.NewGuid().ToString("N"));
        try
        {
            var vanillaDir = Path.Combine(root, "versions", "1.20.1");
            Directory.CreateDirectory(vanillaDir);
            var vanilla = new VersionJson
            {
                Id = "1.20.1",
                MainClass = "net.minecraft.client.main.Main",
                Libraries = new List<Library> { new() { Name = "com.mojang:patchy:1.0" } },
                Arguments = new Arguments { Game = new List<ArgumentItem> { new() { Values = new() { "--username", "${auth_player_name}" } } } }
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
                Arguments = new Arguments { Game = new List<ArgumentItem> { new() { Values = new() { "--fabric" } } } }
            };
            await File.WriteAllTextAsync(Path.Combine(fabricDir, "fabric-1.20.1.json"),
                JsonSerializer.Serialize(fabric, new JsonSerializerOptions { WriteIndented = true }));

            var merged = VersionMerger.Merge(root, "fabric-1.20.1");
            Assert.Equal("net.fabricmc.loader.impl.launcher.Main", merged.MainClass);
            Assert.Contains(merged.Libraries, l => l.Name == "com.mojang:patchy:1.0");
            Assert.Contains(merged.Libraries, l => l.Name == "net.fabricmc:fabric-loader:0.15.0");
            Assert.Contains(merged.Arguments!.Game, i => i.Values.Contains("--fabric"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
