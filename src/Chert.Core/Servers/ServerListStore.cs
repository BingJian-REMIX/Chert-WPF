using System.IO.Compression;
using Chert.Core.Save;

namespace Chert.Core.Servers;

/// <summary>服务器列表条目（对应 servers.dat 中的一项）。</summary>
public class ServerEntry
{
    /// <summary>服务器显示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>地址（host 或 host:port）。</summary>
    public string Address { get; set; } = "";

    /// <summary>Base64 的服务器图标（可空）。</summary>
    public string? Icon { get; set; }

    /// <summary>资源包策略：prompt / enabled / disabled。</summary>
    public string? AcceptTextures { get; set; }

    // ---- 运行时状态（ping 后填充，不写回 servers.dat）----

    /// <summary>延迟（毫秒），-1 表示不可达 / 未测。</summary>
    public int PingMs { get; set; } = -1;

    public int OnlinePlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string? Motd { get; set; }
    public string? VersionName { get; set; }
    public bool Online => PingMs >= 0;

    /// <summary>拆出 host（无端口）。</summary>
    public string Host => SplitAddress(Address).Host;

    /// <summary>拆出端口，缺省 25565。</summary>
    public int Port => SplitAddress(Address).Port;

    /// <summary>延迟等级：0 好(&lt;100) / 1 一般(&lt;300) / 2 差 / 3 不可达。</summary>
    public int LatencyLevel => PingMs < 0 ? 3 : PingMs < 100 ? 0 : PingMs < 300 ? 1 : 2;

    /// <summary>解析 "host:port" / "host" / IPv6 "[::1]:25565"。</summary>
    public static (string Host, int Port) SplitAddress(string? address)
    {
        var a = (address ?? "").Trim();
        if (a.Length == 0) return ("", 25565);

        if (a.StartsWith('['))
        {
            var close = a.IndexOf(']');
            if (close > 0)
            {
                var h6 = a[1..close];
                var rest = a[(close + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p6) && p6 is > 0 and <= 65535)
                    return (h6, p6);
                return (h6, 25565);
            }
        }

        var idx = a.LastIndexOf(':');
        if (idx > 0 && a.IndexOf(':') == idx &&
            int.TryParse(a[(idx + 1)..], out var p) && p is > 0 and <= 65535)
            return (a[..idx], p);

        return (a, 25565);
    }
}

/// <summary>
/// 读写 <c>.minecraft/servers.dat</c>（未压缩 NBT，根 Compound 下的 <c>servers</c> List）。
/// 兼容 gzip 压缩变体：读取时先试原始格式，失败再试 gzip。
/// </summary>
public static class ServerListStore
{
    /// <summary>servers.dat 的默认路径。</summary>
    public static string PathOf(string gameRoot) => Path.Combine(gameRoot, "servers.dat");

    /// <summary>读取服务器列表；文件不存在或损坏时返回空列表（不抛异常）。</summary>
    public static List<ServerEntry> Load(string gameRoot)
    {
        var path = PathOf(gameRoot);
        if (!File.Exists(path)) return new List<ServerEntry>();

        try
        {
            var root = ReadAny(path);
            return FromNbt(root);
        }
        catch
        {
            return new List<ServerEntry>();
        }
    }

    /// <summary>从根标签提取条目。</summary>
    public static List<ServerEntry> FromNbt(NbtTag root)
    {
        var list = new List<ServerEntry>();
        var servers = root.GetChild("servers") ?? root.Find("servers");
        if (servers?.Children is null) return list;

        foreach (var c in servers.Children)
        {
            if (c.Type != NbtTagType.Compound) continue;
            list.Add(new ServerEntry
            {
                Name = c.GetChild("name")?.StringValue ?? "",
                Address = c.GetChild("ip")?.StringValue ?? "",
                Icon = c.GetChild("icon")?.StringValue,
                AcceptTextures = c.GetChild("acceptTextures")?.StringValue
                                 ?? (c.GetChild("acceptTextures")?.Type == NbtTagType.Byte
                                     ? (c.GetChild("acceptTextures")!.ByteValue != 0 ? "enabled" : "disabled")
                                     : null)
            });
        }
        return list;
    }

    /// <summary>构造符合原版结构的 NBT 根标签。</summary>
    public static NbtTag ToNbt(IEnumerable<ServerEntry> entries)
    {
        var root = NbtTag.Compound("");
        var list = new NbtTag
        {
            Type = NbtTagType.List,
            Name = "servers",
            ByteValue = (sbyte)NbtTagType.Compound,
            Children = new List<NbtTag>()
        };

        foreach (var e in entries)
        {
            var c = NbtTag.Compound();
            c.Children!.Add(new NbtTag { Type = NbtTagType.String, Name = "name", StringValue = e.Name });
            c.Children!.Add(new NbtTag { Type = NbtTagType.String, Name = "ip", StringValue = e.Address });
            if (!string.IsNullOrEmpty(e.Icon))
                c.Children!.Add(new NbtTag { Type = NbtTagType.String, Name = "icon", StringValue = e.Icon });
            if (!string.IsNullOrEmpty(e.AcceptTextures))
                c.Children!.Add(new NbtTag { Type = NbtTagType.String, Name = "acceptTextures", StringValue = e.AcceptTextures });
            list.Children!.Add(c);
        }

        root.Children!.Add(list);
        return root;
    }

    /// <summary>写回 servers.dat（未压缩），写前自动备份为 servers.dat.bak。</summary>
    public static bool Save(string gameRoot, IEnumerable<ServerEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(gameRoot);
            var path = PathOf(gameRoot);
            if (File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);

            using var fs = File.Create(path);
            NbtFile.Write(fs, ToNbt(entries));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>新增一条（地址重复则更新名称，返回 false 表示是更新而非新增）。</summary>
    public static bool AddOrUpdate(List<ServerEntry> list, ServerEntry entry)
    {
        var exist = list.FirstOrDefault(s =>
            string.Equals(s.Address, entry.Address, StringComparison.OrdinalIgnoreCase));
        if (exist is null)
        {
            list.Add(entry);
            return true;
        }
        exist.Name = entry.Name;
        exist.Icon = entry.Icon ?? exist.Icon;
        return false;
    }

    /// <summary>在列表中移动条目位置（越界安全）。</summary>
    public static bool Move(List<ServerEntry> list, int from, int to)
    {
        if (from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to) return false;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(to, item);
        return true;
    }

    private static NbtTag ReadAny(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return NbtFile.Read(fs);
        }
        catch
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return NbtFile.Read(gz);
        }
    }
}
