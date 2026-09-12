using System.Globalization;

namespace Chert.Core.Utils;

/// <summary>
/// 语义化版本号比较器：按 <c>.</c> <c>-</c> <c>_</c> <c>+</c> 分段，
/// 数值段按数值比较、非数值段按字典序，数值段恒小于非数值段。
/// 可正确处理 <c>0.9.1</c> &lt; <c>0.10.0</c>、<c>1.20.4-9.0.0</c> &lt; <c>1.20.4-49.0.0</c>、
/// <c>1.20.4-49.0.0</c> &lt; <c>1.20.4-49.1.0</c> 等情形，避免字典序排序导致的选错版本。
/// </summary>
public sealed class VersionComparer : IComparer<string>
{
    /// <summary>共享单例。</summary>
    public static readonly VersionComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        var ax = Split(x);
        var ay = Split(y);
        var n = Math.Max(ax.Length, ay.Length);
        for (var i = 0; i < n; i++)
        {
            var a = i < ax.Length ? ax[i] : "";
            var b = i < ay.Length ? ay[i] : "";
            var aNum = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var na);
            var bNum = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var nb);

            int r;
            if (aNum && bNum) r = na.CompareTo(nb);
            else if (aNum) r = -1;          // 数值段 < 非数值段
            else if (bNum) r = 1;           // 非数值段 > 数值段
            else r = string.CompareOrdinal(a, b);

            if (r != 0) return r;
        }
        return 0;
    }

    private static string[] Split(string v)
        => v.Split(new[] { '.', '-', '_', '+' }, StringSplitOptions.RemoveEmptyEntries);
}
