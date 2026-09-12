using System.Text;
using System.Text.RegularExpressions;

namespace Chert.Core.Utils;

/// <summary>
/// 轻量 Markdown → 纯文本转换（地图详情、Mod 简介等只需要可读文本，不引第三方渲染器）。
/// 保留段落与列表结构，去掉标记符号、代码块围栏、图片与链接语法。
/// </summary>
public static class MarkdownText
{
    private static readonly Regex ImageRx = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex LinkRx = new(@"\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRx = new(@"<[^>]{1,200}>", RegexOptions.Compiled);
    private static readonly Regex HeadingRx = new(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
    private static readonly Regex QuoteRx = new(@"^\s{0,3}>\s?", RegexOptions.Compiled);
    private static readonly Regex BulletRx = new(@"^(\s*)[-*+]\s+", RegexOptions.Compiled);
    private static readonly Regex OrderedRx = new(@"^(\s*)(\d{1,3})[.)]\s+", RegexOptions.Compiled);
    private static readonly Regex RuleRx = new(@"^\s{0,3}([-*_])\s*\1\s*\1[\s\-*_]*$", RegexOptions.Compiled);
    private static readonly Regex EmphasisRx = new(@"(\*{1,3}|_{1,3}|~~)(?=\S)(.+?)(?<=\S)\1", RegexOptions.Compiled);
    private static readonly Regex InlineCodeRx = new(@"`([^`]*)`", RegexOptions.Compiled);
    private static readonly Regex BlankRunRx = new(@"\n{3,}", RegexOptions.Compiled);

    /// <summary>转换为纯文本；输入为空返回空串。<paramref name="maxLength"/> 大于 0 时截断并追加省略号。</summary>
    public static string ToPlainText(string? markdown, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var sb = new StringBuilder();
        var inFence = false;

        foreach (var raw in markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw;

            // 代码块围栏：整块跳过
            if (line.TrimStart().StartsWith("```") || line.TrimStart().StartsWith("~~~"))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;

            // 分隔线统一渲染成一个空行
            if (RuleRx.IsMatch(line)) { sb.Append('\n'); continue; }

            line = ImageRx.Replace(line, "");
            line = LinkRx.Replace(line, "$1");
            line = HtmlTagRx.Replace(line, "");
            line = HeadingRx.Replace(line, "");
            line = QuoteRx.Replace(line, "");
            line = BulletRx.Replace(line, "$1· ");
            line = OrderedRx.Replace(line, "$1$2. ");
            line = InlineCodeRx.Replace(line, "$1");
            line = EmphasisRx.Replace(line, "$2");

            sb.Append(line.TrimEnd()).Append('\n');
        }

        var text = BlankRunRx.Replace(sb.ToString(), "\n\n").Trim();
        if (maxLength > 0 && text.Length > maxLength) text = text[..maxLength].TrimEnd() + "…";
        return text;
    }
}
