using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace Chert.Core.Save;

/// <summary>
/// NBT（Named Binary Tag）标签类型。数值与 Minecraft 规范一致。
/// </summary>
public enum NbtTagType : byte
{
    End = 0,
    Byte = 1,
    Short = 2,
    Int = 3,
    Long = 4,
    Float = 5,
    Double = 6,
    ByteArray = 7,
    String = 8,
    List = 9,
    Compound = 10,
    IntArray = 11,
    LongArray = 12
}

/// <summary>
/// 一个 NBT 标签节点。所有值类型共用同一节点，按 <see cref="Type"/> 选择对应 *Value 属性。
/// <list type="bullet">
///   <item><description>Compound / List 的子节点存放在 <see cref="Children"/>，顺序原样保留。</description></item>
///   <item><description>Compound 子节点带 <see cref="Name"/>；List 子节点 Name 为 null 且类型一致。</description></item>
/// </list>
/// 设计目标：完整保留原始结构，便于"改写某个值后无损写回"（如 level.dat 的 DataVersion）。
/// </summary>
public class NbtTag
{
    public NbtTagType Type { get; set; }

    /// <summary>标签名（TAG_End / List 元素为 null）。</summary>
    public string? Name { get; set; }

    public sbyte ByteValue { get; set; }
    public short ShortValue { get; set; }
    public int IntValue { get; set; }
    public long LongValue { get; set; }
    public float FloatValue { get; set; }
    public double DoubleValue { get; set; }
    public byte[]? ByteArrayValue { get; set; }
    public string? StringValue { get; set; }
    public List<NbtTag>? Children { get; set; }
    public int[]? IntArrayValue { get; set; }
    public long[]? LongArrayValue { get; set; }

    // ---- 便捷构造 ----

    public static NbtTag Compound(string? name = null) =>
        new() { Type = NbtTagType.Compound, Name = name, Children = new List<NbtTag>() };

    public static NbtTag Int(string name, int value) =>
        new() { Type = NbtTagType.Int, Name = name, IntValue = value };

    public static NbtTag Long(string name, long value) =>
        new() { Type = NbtTagType.Long, Name = name, LongValue = value };

    // ---- 便捷访问 ----

    /// <summary>在 Compound 子节点中按名查找直接子节点。</summary>
    public NbtTag? GetChild(string name) =>
        Children?.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    /// <summary>递归查找第一个名为 <paramref name="name"/> 的标签（深度优先）。</summary>
    public NbtTag? Find(string name)
    {
        if (string.Equals(Name, name, StringComparison.Ordinal)) return this;
        if (Children is null) return null;
        foreach (var c in Children)
        {
            var hit = c.Find(name);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>读取 level.dat 的 DataVersion（找不到返回 0）。</summary>
    public int GetDataVersion() => Find("DataVersion")?.IntValue ?? 0;

    /// <summary>递归查找并改写名为 DataVersion 的整型标签；找到并返回 true。</summary>
    public bool TrySetDataVersion(int value)
    {
        var node = Find("DataVersion");
        if (node is null || node.Type != NbtTagType.Int) return false;
        node.IntValue = value;
        return true;
    }
}

/// <summary>
/// 轻量 NBT 读写器：仅依赖 BCL（<see cref="System.IO.Compression.GZipStream"/>、<see cref="System.Buffers.Binary.BinaryPrimitives"/>）。
/// 采用大端（网络字节序），支持全部 13 种标签；round-trip 字节无损（Float/Double 按大端原样读写）。
/// 典型用途：读取 / 写回 Minecraft <c>level.dat</c> 的 <c>DataVersion</c>。
/// </summary>
public static class NbtFile
{
    /// <summary>从 gzip 压缩的 NBT 文件读取根 Compound。</summary>
    public static NbtTag ReadGzip(string path)
    {
        using var fs = File.OpenRead(path);
        using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        return Read(gzip);
    }

    /// <summary>从流读取根 Compound（调用方负责解压）。</summary>
    public static NbtTag Read(Stream s)
    {
        var root = ReadTag(s, readName: true);
        return root;
    }

    /// <summary>将根 Compound 以 gzip 压缩写入文件。</summary>
    public static void WriteGzip(string path, NbtTag root)
    {
        using var fs = File.Create(path);
        using var gzip = new GZipStream(fs, CompressionMode.Compress);
        Write(gzip, root);
    }

    /// <summary>将根 Compound 写入流（调用方负责压缩）。</summary>
    public static void Write(Stream s, NbtTag root) => WriteTag(s, root, writeName: true);

    // ---- 读取 ----

    private static NbtTag ReadTag(Stream s, bool readName)
    {
        var type = (NbtTagType)s.ReadByte();
        string? name = null;
        if (readName && type != NbtTagType.End)
            name = ReadUtf8(s, ReadUInt16BE(s));

        var tag = new NbtTag { Type = type, Name = name };
        ReadValue(s, tag);
        return tag;
    }

    private static void ReadValue(Stream s, NbtTag tag)
    {
        switch (tag.Type)
        {
            case NbtTagType.End:
                break;
            case NbtTagType.Byte:
                tag.ByteValue = (sbyte)s.ReadByte();
                break;
            case NbtTagType.Short:
                tag.ShortValue = ReadInt16BE(s);
                break;
            case NbtTagType.Int:
                tag.IntValue = ReadInt32BE(s);
                break;
            case NbtTagType.Long:
                tag.LongValue = ReadInt64BE(s);
                break;
            case NbtTagType.Float:
                tag.FloatValue = ReadSingleBE(s);
                break;
            case NbtTagType.Double:
                tag.DoubleValue = ReadDoubleBE(s);
                break;
            case NbtTagType.ByteArray:
            {
                var len = ReadInt32BE(s);
                tag.ByteArrayValue = ReadBytes(s, len);
                break;
            }
            case NbtTagType.String:
                tag.StringValue = ReadUtf8(s, ReadUInt16BE(s));
                break;
            case NbtTagType.List:
            {
                var elemType = (NbtTagType)s.ReadByte();
                var count = ReadInt32BE(s);
                var list = new List<NbtTag>(count);
                for (var i = 0; i < count; i++)
                {
                    var child = new NbtTag { Type = elemType };
                    ReadValue(s, child);
                    list.Add(child);
                }
                tag.Children = list;
                break;
            }
            case NbtTagType.Compound:
            {
                var children = new List<NbtTag>();
                while (true)
                {
                    var t = (NbtTagType)s.ReadByte();
                    if (t == NbtTagType.End) break;
                    var cname = ReadUtf8(s, ReadUInt16BE(s));
                    var child = new NbtTag { Type = t, Name = cname };
                    ReadValue(s, child);
                    children.Add(child);
                }
                tag.Children = children;
                break;
            }
            case NbtTagType.IntArray:
            {
                var len = ReadInt32BE(s);
                var arr = new int[len];
                for (var i = 0; i < len; i++) arr[i] = ReadInt32BE(s);
                tag.IntArrayValue = arr;
                break;
            }
            case NbtTagType.LongArray:
            {
                var len = ReadInt32BE(s);
                var arr = new long[len];
                for (var i = 0; i < len; i++) arr[i] = ReadInt64BE(s);
                tag.LongArrayValue = arr;
                break;
            }
            default:
                throw new InvalidOperationException($"不支持的 NBT 标签类型：{tag.Type}");
        }
    }

    // ---- 写入 ----

    private static void WriteTag(Stream s, NbtTag tag, bool writeName)
    {
        s.WriteByte((byte)tag.Type);
        if (writeName)
        {
            var nameBytes = Encoding.UTF8.GetBytes(tag.Name ?? "");
            WriteUInt16BE(s, (ushort)nameBytes.Length);
            s.Write(nameBytes, 0, nameBytes.Length);
        }
        WriteValue(s, tag);
    }

    private static void WriteValue(Stream s, NbtTag tag)
    {
        switch (tag.Type)
        {
            case NbtTagType.End:
                break;
            case NbtTagType.Byte:
                s.WriteByte((byte)tag.ByteValue);
                break;
            case NbtTagType.Short:
                WriteInt16BE(s, tag.ShortValue);
                break;
            case NbtTagType.Int:
                WriteInt32BE(s, tag.IntValue);
                break;
            case NbtTagType.Long:
                WriteInt64BE(s, tag.LongValue);
                break;
            case NbtTagType.Float:
                s.Write(ToBigEndian(tag.FloatValue), 0, 4);
                break;
            case NbtTagType.Double:
                s.Write(ToBigEndian(tag.DoubleValue), 0, 8);
                break;
            case NbtTagType.ByteArray:
            {
                var bytes = tag.ByteArrayValue ?? Array.Empty<byte>();
                WriteInt32BE(s, bytes.Length);
                s.Write(bytes, 0, bytes.Length);
                break;
            }
            case NbtTagType.String:
            {
                var bytes = Encoding.UTF8.GetBytes(tag.StringValue ?? "");
                WriteUInt16BE(s, (ushort)bytes.Length);
                s.Write(bytes, 0, bytes.Length);
                break;
            }
            case NbtTagType.List:
            {
                var list = tag.Children ?? new List<NbtTag>();
                var elemType = list.Count > 0 ? list[0].Type : NbtTagType.End;
                s.WriteByte((byte)elemType);
                WriteInt32BE(s, list.Count);
                // 列表元素不写类型标签（类型由列表头声明），只写值
                foreach (var c in list) WriteValue(s, c);
                break;
            }
            case NbtTagType.Compound:
            {
                foreach (var c in tag.Children ?? Enumerable.Empty<NbtTag>())
                    WriteTag(s, c, writeName: true);
                s.WriteByte((byte)NbtTagType.End);
                break;
            }
            case NbtTagType.IntArray:
            {
                var arr = tag.IntArrayValue ?? Array.Empty<int>();
                WriteInt32BE(s, arr.Length);
                foreach (var v in arr) WriteInt32BE(s, v);
                break;
            }
            case NbtTagType.LongArray:
            {
                var arr = tag.LongArrayValue ?? Array.Empty<long>();
                WriteInt32BE(s, arr.Length);
                foreach (var v in arr) WriteInt64BE(s, v);
                break;
            }
            default:
                throw new InvalidOperationException($"不支持的 NBT 标签类型：{tag.Type}");
        }
    }

    // ---- 大端基本类型 ----

    private static byte[] ReadBytes(Stream s, int len)
    {
        if (len < 0) throw new InvalidOperationException("NBT 长度字段为负");
        var buf = new byte[len];
        var read = 0;
        while (read < len)
        {
            var n = s.Read(buf, read, len - read);
            if (n == 0) throw new EndOfStreamException("NBT 数据意外结束");
            read += n;
        }
        return buf;
    }

    private static ushort ReadUInt16BE(Stream s) => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(s, 2));
    private static short ReadInt16BE(Stream s) => BinaryPrimitives.ReadInt16BigEndian(ReadBytes(s, 2));
    private static int ReadInt32BE(Stream s) => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(s, 4));
    private static long ReadInt64BE(Stream s) => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(s, 8));

    private static float ReadSingleBE(Stream s)
    {
        var b = ReadBytes(s, 4);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToSingle(b, 0);
    }

    private static double ReadDoubleBE(Stream s)
    {
        var b = ReadBytes(s, 8);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return BitConverter.ToDouble(b, 0);
    }

    private static string ReadUtf8(Stream s, int len) => Encoding.UTF8.GetString(ReadBytes(s, len));

    private static void WriteUInt16BE(Stream s, ushort v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        s.Write(b, 0, 2);
    }

    private static void WriteInt16BE(Stream s, short v)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, v);
        s.Write(b, 0, 2);
    }

    private static void WriteInt32BE(Stream s, int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        s.Write(b, 0, 4);
    }

    private static void WriteInt64BE(Stream s, long v)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(b, v);
        s.Write(b, 0, 8);
    }

    private static byte[] ToBigEndian(float v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return b;
    }

    private static byte[] ToBigEndian(double v)
    {
        var b = BitConverter.GetBytes(v);
        if (BitConverter.IsLittleEndian) Array.Reverse(b);
        return b;
    }
}
