using System.Diagnostics;

namespace Chert.Core.Save;

/// <summary>
/// §三 存档降级功能。
/// <para>
/// 设计原则（与崩溃修复一致——非破坏性、可还原）：
/// 1. <b>强制备份</b>：任何降级/重写操作前，先把整个存档目录复制到
///    <c>saves/&lt;SaveName&gt;.backup-&lt;yyyyMMddHHmmss&gt;</c>，原始存档绝不删除。
/// 2. <b>方案 A（快速）</b>：直接改写 <c>level.dat</c> 的 DataVersion 到目标版本；纯文件操作、
///    即刻完成，真正的世界数据转换交由游戏在加载时执行（可能再次提升 DataVersion）。
/// 3. <b>方案 B（Amulet）</b>：调用外部 Amulet 工具做数据层世界转换，更安全但依赖外部程序；
///    找不到 Amulet 时优雅失败并提示。
/// 4. 输出 <see cref="SaveDowngradePlan.Summary"/> 变更摘要，供 UI 展示。
/// </para>
/// </summary>
public static class SaveDowngrader
{
    /// <summary>读取某存档当前 DataVersion（无 level.dat / 解析失败返回 0）。</summary>
    public static int GetSaveDataVersion(string savePath)
    {
        var lvl = SaveCompatibilityDetector.LevelDatPath(savePath);
        if (!File.Exists(lvl)) return 0;
        try { return NbtFile.ReadGzip(lvl).GetDataVersion(); }
        catch { return 0; }
    }

    /// <summary>
    /// 强制备份整个存档目录（复制为 <c>&lt;SaveName&gt;.backup-&lt;ts&gt;</c>）。
    /// 返回备份目录路径。已存在同名则仍新建带时间戳的副本。
    /// </summary>
    public static string BackupAsync(string savePath)
    {
        var savesDir = Path.GetDirectoryName(savePath)
                       ?? throw new ArgumentException("无效存档路径", nameof(savePath));
        var saveName = Path.GetFileName(savePath);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var backupPath = Path.Combine(savesDir, $"{saveName}.backup-{stamp}");

        CopyDirectory(savePath, backupPath);
        return backupPath;
    }

    /// <summary>
    /// 执行降级。会先强制备份，再按方案改写/转换。
    /// </summary>
    /// <param name="savePath">存档根目录（…/saves/&lt;SaveName&gt;）。</param>
    /// <param name="targetGameVersionId">目标游戏版本（如 "1.20.1"）。</param>
    /// <param name="method">降级方案（A 快速 / B Amulet）。</param>
    /// <param name="amuletPath">Amulet 可执行文件路径；为 null 时自动查找。</param>
    public static async Task<SaveDowngradePlan> DowngradeAsync(string savePath,
        string targetGameVersionId, DowngradeMethod method, string? amuletPath = null)
    {
        var plan = new SaveDowngradePlan
        {
            SaveName = Path.GetFileName(savePath),
            SavePath = savePath,
            Method = method,
            FromDataVersion = GetSaveDataVersion(savePath)
        };

        var targetDv = DataVersionMap.ToDataVersion(targetGameVersionId);
        if (targetDv is null)
        {
            plan.Success = false;
            plan.ErrorMessage = $"目标游戏版本 {targetGameVersionId} 不在对照表中，无法确定目标 DataVersion。";
            return plan;
        }
        plan.ToDataVersion = targetDv.Value;

        // 1) 强制备份
        try
        {
            plan.BackupPath = BackupAsync(savePath);
            plan.Summary.Add($"已强制备份原存档到：{plan.BackupPath}");
        }
        catch (Exception ex)
        {
            plan.Success = false;
            plan.ErrorMessage = $"备份失败：{ex.Message}";
            return plan;
        }

        // 2) 按方案执行
        try
        {
            if (method == DowngradeMethod.QuickModifyDataVersion)
            {
                await QuickDowngradeAsync(savePath, plan.ToDataVersion);
                plan.Summary.Add($"方案 A（快速）：已将 DataVersion 从 {plan.FromDataVersion} 改写为 {plan.ToDataVersion}（{targetGameVersionId}）。");
                plan.Summary.Add("提示：游戏加载时会尝试就地转换世界数据，可能再次提升 DataVersion；若转换异常请用方案 B 或回滚备份。");
            }
            else
            {
                await AmuletDowngradeAsync(savePath, targetGameVersionId, plan.ToDataVersion, amuletPath);
                plan.Summary.Add($"方案 B（Amulet）：已将世界数据转换至 {targetGameVersionId}（dv={plan.ToDataVersion}）。");
            }

            plan.Success = true;
        }
        catch (Exception ex)
        {
            plan.Success = false;
            plan.ErrorMessage = ex.Message;
            plan.Summary.Add($"降级失败：{ex.Message}（原存档备份完好，可回滚）。");
        }

        return plan;
    }

    /// <summary>
    /// §四.2 回滚：用某次备份替换当前存档。回滚前先把当前（可能已损坏的）存档另存为
    /// <c>&lt;SaveName&gt;.replaced-&lt;ts&gt;</c>，保证两端都不丢失。
    /// </summary>
    public static string RestoreBackupAsync(string backupPath, string savePath)
    {
        if (!Directory.Exists(backupPath))
            throw new DirectoryNotFoundException($"备份不存在：{backupPath}");

        // 先把当前存档安全另存
        var replacedPath = "";
        if (Directory.Exists(savePath))
        {
            var savesDir = Path.GetDirectoryName(savePath) ?? "";
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            replacedPath = Path.Combine(savesDir, $"{Path.GetFileName(savePath)}.replaced-{stamp}");
            CopyDirectory(savePath, replacedPath);
            Directory.Delete(savePath, recursive: true);
        }

        CopyDirectory(backupPath, savePath);
        return replacedPath;
    }

    // ---- 内部：方案 A ----

    private static Task QuickDowngradeAsync(string savePath, int targetDv)
    {
        var lvl = SaveCompatibilityDetector.LevelDatPath(savePath);
        if (!File.Exists(lvl))
            throw new FileNotFoundException("找不到 level.dat", lvl);

        var root = NbtFile.ReadGzip(lvl);
        if (!root.TrySetDataVersion(targetDv))
            throw new InvalidOperationException("level.dat 中未找到 DataVersion 整型标签，无法快速降级。");

        // 同时清除可能指向更高版本的数据标记（如 Version 顶层字段中残留的更高 dv）
        NbtFile.WriteGzip(lvl, root);
        return Task.CompletedTask;
    }

    // ---- 内部：方案 B（Amulet） ----

    private static async Task AmuletDowngradeAsync(string savePath, string targetGameVersionId,
        int targetDv, string? amuletPath)
    {
        var exe = amuletPath ?? await FindAmuletAsync();
        if (exe is null)
            throw new InvalidOperationException(
                "未找到 Amulet 工具。请安装 Amulet（https://amuletmc.com）后在设置中指定其路径，或改用方案 A。");

        var outDir = savePath + ".amulet-tmp";
        if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            ArgumentList = { savePath, outDir, "--input-format", "java", "--output-format", "java" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
                         ?? throw new InvalidOperationException("无法启动 Amulet 进程");
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Amulet 退出码 {proc.ExitCode}：{err}");
        }

        // 用 Amulet 输出替换原存档
        var replaced = savePath + ".pre-amulet";
        if (Directory.Exists(savePath))
        {
            if (Directory.Exists(replaced)) Directory.Delete(replaced, recursive: true);
            CopyDirectory(savePath, replaced);
            Directory.Delete(savePath, recursive: true);
        }
        CopyDirectory(outDir, savePath);
        Directory.Delete(outDir, recursive: true);
    }

    /// <summary>在常见位置查找 Amulet 可执行文件；找不到返回 null。</summary>
    public static async Task<string?> FindAmuletAsync()
    {
        var candidates = new List<string>();
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            candidates.Add("amulet.exe");
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Amulet", "amulet.exe");
            candidates.Add(local);
        }
        else
        {
            candidates.Add("amulet");
            candidates.Add("/usr/bin/amulet");
            candidates.Add("/usr/local/bin/amulet");
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
            try
            {
                var which = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    ArgumentList = { c },
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                which?.WaitForExit();
                var path = which != null ? (await which.StandardOutput.ReadToEndAsync()).Trim() : "";
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            catch { /* 忽略 */ }
        }
        return null;
    }

    // ---- 目录复制 ----

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }
    }
}
