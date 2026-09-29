using System.Globalization;
using System.Windows.Media;

namespace Chert.App.Controls;

/// <summary>
/// HSL ⇄ RGB 换算与 HEX 解析工具。
/// 清单 #50：皮肤编辑器全色域取色器；清单 #59：同一取色器复用于主题色选择。
/// </summary>
public static class ColorMath
{
    /// <summary>色域平面固定采用的明度，保证平面始终呈现完整鲜艳色域。</summary>
    public const double PlaneLightness = 0.5;

    /// <summary>RGB 转 HSL，h / s / l 均归一化到 0~1。</summary>
    public static void ToHsl(Color c, out double h, out double s, out double l)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;

        var d = max - min;
        if (d < 1e-6)
        {
            h = 0;
            s = 0;
            return;
        }

        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        if (Math.Abs(max - r) < 1e-6)
            h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
        else if (Math.Abs(max - g) < 1e-6)
            h = ((b - r) / d + 2) / 6.0;
        else
            h = ((r - g) / d + 4) / 6.0;
    }

    /// <summary>HSL 转 RGB，h / s / l 均按 0~1 处理（超出会先归一化）。</summary>
    public static Color FromHsl(double h, double s, double l, byte a = 255)
    {
        h = ((h % 1.0) + 1.0) % 1.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        if (s <= 1e-6)
        {
            var v = (byte)Math.Round(l * 255.0);
            return Color.FromArgb(a, v, v, v);
        }

        var q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
        var p = 2.0 * l - q;
        var rr = HueToChannel(p, q, h + 1.0 / 3.0);
        var gg = HueToChannel(p, q, h);
        var bb = HueToChannel(p, q, h - 1.0 / 3.0);
        return Color.FromArgb(a,
            (byte)Math.Round(rr * 255.0),
            (byte)Math.Round(gg * 255.0),
            (byte)Math.Round(bb * 255.0));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1.0;
        if (t > 1) t -= 1.0;
        if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
        if (t < 1.0 / 2.0) return q;
        if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
        return p;
    }

    public static string ToHex(Color c)
        => c.A == 255
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>
    /// 解析 #RGB / #RRGGBB / #AARRGGBB 形式（井号可省略，大小写不敏感）。
    /// </summary>
    public static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().TrimStart('#').Trim();
        if (t.Length == 3)
        {
            if (!byte.TryParse(t[0].ToString() + t[0], NumberStyles.HexNumber, null, out var r)) return false;
            if (!byte.TryParse(t[1].ToString() + t[1], NumberStyles.HexNumber, null, out var g)) return false;
            if (!byte.TryParse(t[2].ToString() + t[2], NumberStyles.HexNumber, null, out var b)) return false;
            color = Color.FromRgb(r, g, b);
            return true;
        }
        if (t.Length is 6 or 8)
        {
            if (!uint.TryParse(t, NumberStyles.HexNumber, null, out var v)) return false;
            color = t.Length == 6
                ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }
        return false;
    }
}
