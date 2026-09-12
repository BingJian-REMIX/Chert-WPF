using System.Security.Cryptography;

namespace Chert.Core.Utils;

/// <summary>哈希与文件校验工具。</summary>
public static class HashUtil
{
    /// <summary>计算文件 SHA-1（小写十六进制）。</summary>
    public static string Sha1(string filePath)
    {
        using var sha = SHA1.Create();
        using var fs = File.OpenRead(filePath);
        var bytes = sha.ComputeHash(fs);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>校验 SHA-1；expected 为空时直接通过。</summary>
    public static bool VerifySha1(string filePath, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        if (!File.Exists(filePath)) return false;
        return Sha1(filePath).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>校验文件大小（字节）。expected 为 null 时通过。</summary>
    public static bool VerifySize(string filePath, long? expected)
    {
        if (expected is null) return true;
        if (!File.Exists(filePath)) return false;
        return new FileInfo(filePath).Length == expected.Value;
    }

    /// <summary>是否为 ZIP 文件（魔术字节 PK\x03\x04）。</summary>
    public static bool IsZip(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var magic = new byte[4];
            return fs.Read(magic, 0, 4) == 4
                   && magic[0] == 0x50 && magic[1] == 0x4B
                   && magic[2] == 0x03 && magic[3] == 0x04;
        }
        catch
        {
            return false;
        }
    }
}
