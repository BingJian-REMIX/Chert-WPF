using System.Globalization;
using System.Text;
using Chert.Core.Save;

namespace Chert.Core.Toolbox;

/// <summary>NBT 编辑操作结果。</summary>
public sealed class NbtEditResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public NbtTag? Tag { get; init; }

    public static NbtEditResult Fail(string error) => new() { Ok = false, Error = error };
    public static NbtEditResult Success(NbtTag? tag = null) => new() { Ok = true, Tag = tag };
}

/// <summary>NBT 保存结果。</summary>
public sealed class NbtSaveResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    /// <summary>自动备份出的原文件路径（未备份时为 null）。</summary>
    public string? BackupPath { get; init; }

    public long SizeBytes { get; init; }

    public static NbtSaveResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// NBT 编辑器（工具箱开发工具）：在已有 <see cref="NbtFile"/> 读写能力之上，
/// 提供路径寻址、类型安全赋值、增删改与树形展示。
/// <para>路径语法：<c>Data.Player.Pos[0]</c> —— 点号进 Compound，方括号进 List。</para>
/// </summary>
public static class NbtEditor
{
    /// <summary>按路径查找标签；找不到返回 null。</summary>
    public static NbtTag? Resolve(NbtTag root, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return root;

        var current = root;
        foreach (var token in Tokenize(path!))
        {
            if (current is null) return null;

            if (token.IsIndex)
            {
                if (current.Type != NbtTagType.List || current.Children is null) return null;
                if (token.Index < 0 || token.Index >= current.Children.Count) return null;
                current = current.Children[token.Index];
            }
            else
            {
                if (current.Children is null) return null;
                current = current.Children.FirstOrDefault(c =>
                    string.Equals(c.Name, token.Name, StringComparison.Ordinal));
            }
        }
        return current;
    }

    /// <summary>取标签的显示值（用于树形界面右列）。</summary>
    public static string ValueText(NbtTag tag) => tag.Type switch
    {
        NbtTagType.Byte => tag.ByteValue.ToString(CultureInfo.InvariantCulture),
        NbtTagType.Short => tag.ShortValue.ToString(CultureInfo.InvariantCulture),
        NbtTagType.Int => tag.IntValue.ToString(CultureInfo.InvariantCulture),
        NbtTagType.Long => tag.LongValue.ToString(CultureInfo.InvariantCulture),
        NbtTagType.Float => tag.FloatValue.ToString("R", CultureInfo.InvariantCulture),
        NbtTagType.Double => tag.DoubleValue.ToString("R", CultureInfo.InvariantCulture),
        NbtTagType.String => tag.StringValue ?? "",
        NbtTagType.ByteArray => $"[{tag.ByteArrayValue?.Length ?? 0} 字节]",
        NbtTagType.IntArray => $"[{tag.IntArrayValue?.Length ?? 0} 个 int]",
        NbtTagType.LongArray => $"[{tag.LongArrayValue?.Length ?? 0} 个 long]",
        NbtTagType.List => $"[{tag.Children?.Count ?? 0} 项]",
        NbtTagType.Compound => $"{{{tag.Children?.Count ?? 0} 项}}",
        _ => ""
    };

    /// <summary>是否为可直接编辑的标量类型。</summary>
    public static bool IsScalar(NbtTagType type) => type is
        NbtTagType.Byte or NbtTagType.Short or NbtTagType.Int or NbtTagType.Long or
        NbtTagType.Float or NbtTagType.Double or NbtTagType.String;

    /// <summary>按路径赋值（仅标量）。字符串会按目标类型解析，越界 / 格式错返回失败。</summary>
    public static NbtEditResult SetValue(NbtTag root, string path, string rawValue)
    {
        var tag = Resolve(root, path);
        if (tag is null) return NbtEditResult.Fail($"路径不存在：{path}");
        if (!IsScalar(tag.Type)) return NbtEditResult.Fail($"{tag.Type} 不是可直接编辑的标量类型");

        try
        {
            switch (tag.Type)
            {
                case NbtTagType.Byte:
                    if (!sbyte.TryParse(rawValue, out var b)) return NbtEditResult.Fail("应为 -128~127 的整数");
                    tag.ByteValue = b;
                    break;
                case NbtTagType.Short:
                    if (!short.TryParse(rawValue, out var s)) return NbtEditResult.Fail("应为 -32768~32767 的整数");
                    tag.ShortValue = s;
                    break;
                case NbtTagType.Int:
                    if (!int.TryParse(rawValue, out var i)) return NbtEditResult.Fail("应为 32 位整数");
                    tag.IntValue = i;
                    break;
                case NbtTagType.Long:
                    if (!long.TryParse(rawValue, out var l)) return NbtEditResult.Fail("应为 64 位整数");
                    tag.LongValue = l;
                    break;
                case NbtTagType.Float:
                    if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                        return NbtEditResult.Fail("应为单精度浮点数");
                    tag.FloatValue = f;
                    break;
                case NbtTagType.Double:
                    if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        return NbtEditResult.Fail("应为双精度浮点数");
                    tag.DoubleValue = d;
                    break;
                case NbtTagType.String:
                    tag.StringValue = rawValue;
                    break;
            }
            return NbtEditResult.Success(tag);
        }
        catch (Exception ex)
        {
            return NbtEditResult.Fail(ex.Message);
        }
    }

    /// <summary>在 Compound 下新增子标签；同名已存在返回失败。</summary>
    public static NbtEditResult AddChild(NbtTag root, string parentPath, string name, NbtTagType type)
    {
        var parent = Resolve(root, parentPath);
        if (parent is null) return NbtEditResult.Fail($"路径不存在：{parentPath}");
        if (parent.Type != NbtTagType.Compound) return NbtEditResult.Fail("只能在 Compound 下新增命名子标签");
        if (string.IsNullOrWhiteSpace(name)) return NbtEditResult.Fail("标签名不能为空");

        parent.Children ??= new List<NbtTag>();
        if (parent.Children.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
            return NbtEditResult.Fail($"已存在同名标签：{name}");

        var tag = NewTag(type, name);
        parent.Children.Add(tag);
        return NbtEditResult.Success(tag);
    }

    /// <summary>删除路径指向的标签（不能删根）。</summary>
    public static NbtEditResult Remove(NbtTag root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return NbtEditResult.Fail("不能删除根标签");

        var tokens = Tokenize(path).ToList();
        if (tokens.Count == 0) return NbtEditResult.Fail("路径无效");

        var parentPath = string.Join("", tokens.Take(tokens.Count - 1).Select(t => t.ToPathPart()));
        var parent = Resolve(root, parentPath.TrimStart('.'));
        if (parent?.Children is null) return NbtEditResult.Fail("父节点不存在");

        var last = tokens[^1];
        if (last.IsIndex)
        {
            if (last.Index < 0 || last.Index >= parent.Children.Count) return NbtEditResult.Fail("索引越界");
            var removed = parent.Children[last.Index];
            parent.Children.RemoveAt(last.Index);
            return NbtEditResult.Success(removed);
        }

        var target = parent.Children.FirstOrDefault(c => string.Equals(c.Name, last.Name, StringComparison.Ordinal));
        if (target is null) return NbtEditResult.Fail($"标签不存在：{last.Name}");
        parent.Children.Remove(target);
        return NbtEditResult.Success(target);
    }

    /// <summary>重命名标签（List 元素不可命名）。</summary>
    public static NbtEditResult Rename(NbtTag root, string path, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return NbtEditResult.Fail("新名称不能为空");
        var tag = Resolve(root, path);
        if (tag is null) return NbtEditResult.Fail($"路径不存在：{path}");
        if (tag.Name is null) return NbtEditResult.Fail("List 元素不能命名");
        tag.Name = newName;
        return NbtEditResult.Success(tag);
    }

    /// <summary>
    /// 生成备份文件名：<c>原名.yyyyMMdd-HHmmss.bak</c>。
    /// </summary>
    public static string BuildBackupPath(string originalPath, DateTime time) =>
        originalPath + $".{time:yyyyMMdd-HHmmss}.bak";

    /// <summary>
    /// 保存 NBT 到文件。规格要求"保存自动备份原文件"——先把原文件复制成
    /// <c>.bak</c>，写入失败时自动回滚，保证不会写出半个文件。
    /// </summary>
    public static NbtSaveResult Save(NbtTag root, string path, bool backup = true)
    {
        if (string.IsNullOrWhiteSpace(path)) return NbtSaveResult.Fail("路径为空");

        string? backupPath = null;
        try
        {
            if (backup && File.Exists(path))
            {
                backupPath = BuildBackupPath(path, DateTime.Now);
                File.Copy(path, backupPath, overwrite: true);
            }

            // 先写临时文件，成功后再替换，避免中途失败损坏原档
            var tmp = path + ".tmp";
            NbtFile.WriteGzip(tmp, root);

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);

            return new NbtSaveResult
            {
                Ok = true,
                BackupPath = backupPath,
                SizeBytes = new FileInfo(path).Length
            };
        }
        catch (Exception ex)
        {
            // 回滚：原文件还在备份里就还原回去
            try
            {
                if (backupPath is not null && File.Exists(backupPath) && !File.Exists(path))
                    File.Copy(backupPath, path, overwrite: true);
                var tmp = path + ".tmp";
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch { /* 回滚失败时保留备份文件，用户可手动恢复 */ }

            return NbtSaveResult.Fail(ex.Message);
        }
    }

    /// <summary>读取 NBT 文件；失败返回 null。</summary>
    public static NbtTag? Load(string path)
    {
        try
        {
            return File.Exists(path) ? NbtFile.ReadGzip(path) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>统计标签总数（含自身）。</summary>
    public static int CountNodes(NbtTag tag)
    {
        var n = 1;
        if (tag.Children is not null)
            foreach (var c in tag.Children)
                n += CountNodes(c);
        return n;
    }

    /// <summary>渲染为可读文本树（用于导出 / 差异对比）。</summary>
    public static string RenderTree(NbtTag root, int maxDepth = 8)
    {
        var sb = new StringBuilder();
        Render(root, sb, 0, maxDepth);
        return sb.ToString();
    }

    /// <summary>创建指定类型的空标签。</summary>
    public static NbtTag NewTag(NbtTagType type, string? name) => type switch
    {
        NbtTagType.Compound => NbtTag.Compound(name),
        NbtTagType.List => new NbtTag { Type = NbtTagType.List, Name = name, Children = new List<NbtTag>() },
        NbtTagType.ByteArray => new NbtTag { Type = type, Name = name, ByteArrayValue = Array.Empty<byte>() },
        NbtTagType.IntArray => new NbtTag { Type = type, Name = name, IntArrayValue = Array.Empty<int>() },
        NbtTagType.LongArray => new NbtTag { Type = type, Name = name, LongArrayValue = Array.Empty<long>() },
        NbtTagType.String => new NbtTag { Type = type, Name = name, StringValue = "" },
        _ => new NbtTag { Type = type, Name = name }
    };

    private static void Render(NbtTag tag, StringBuilder sb, int depth, int maxDepth)
    {
        sb.Append(new string(' ', depth * 2));
        sb.Append(tag.Type);
        if (tag.Name is not null) sb.Append(' ').Append(tag.Name);
        sb.Append(": ").AppendLine(ValueText(tag));

        if (tag.Children is null || depth >= maxDepth) return;
        foreach (var c in tag.Children) Render(c, sb, depth + 1, maxDepth);
    }

    private readonly struct PathToken
    {
        public PathToken(string name) { Name = name; Index = -1; IsIndex = false; }
        public PathToken(int index) { Name = ""; Index = index; IsIndex = true; }

        public string Name { get; }
        public int Index { get; }
        public bool IsIndex { get; }

        public string ToPathPart() => IsIndex ? $"[{Index}]" : $".{Name}";
    }

    private static IEnumerable<PathToken> Tokenize(string path)
    {
        var buffer = new StringBuilder();
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '.')
            {
                if (buffer.Length > 0) { yield return new PathToken(buffer.ToString()); buffer.Clear(); }
            }
            else if (c == '[')
            {
                if (buffer.Length > 0) { yield return new PathToken(buffer.ToString()); buffer.Clear(); }
                var close = path.IndexOf(']', i);
                if (close < 0) yield break;
                var inner = path[(i + 1)..close];
                yield return int.TryParse(inner, out var idx) ? new PathToken(idx) : new PathToken(-1);
                i = close;
            }
            else
            {
                buffer.Append(c);
            }
        }
        if (buffer.Length > 0) yield return new PathToken(buffer.ToString());
    }
}
