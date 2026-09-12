using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Chert.App.Views;

/// <summary>极简 Markdown → FlowDocument 渲染器。覆盖 AI 回复常见子集：标题、有序/无序列表、加粗、斜体、行内代码、围栏代码块、换行。
/// 解析失败由调用方（MarkdownTextBlock）兜底为纯文本段落。</summary>
internal static class MarkdownParser
{
    public static void Fill(FlowDocument doc, string text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var listItems = new List<(string Text, bool Ordered)>();
        var codeLines = new List<string>();
        bool inCode = false;

        void FlushList()
        {
            if (listItems.Count == 0) return;
            bool ordered = listItems[0].Ordered;
            var list = new List
            {
                MarkerStyle = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                Margin = new Thickness(18, 2, 0, 8)
            };
            foreach (var (t, _) in listItems)
                list.ListItems.Add(new ListItem(new Paragraph(new Run(t))));
            doc.Blocks.Add(list);
            listItems.Clear();
        }

        void FlushCode()
        {
            if (codeLines.Count == 0) return;
            var tb = new TextBlock
            {
                Text = string.Join("\n", codeLines),
                FontFamily = new FontFamily("Consolas, Menlo, Monaco, Courier New, monospace"),
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(2)
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeground");
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(26, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 4, 0, 8),
                Padding = new Thickness(10),
                Child = tb
            };
            border.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            doc.Blocks.Add(new BlockUIContainer(border));
            codeLines.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (inCode)
            {
                if (trimmed == "```") { inCode = false; FlushCode(); }
                else codeLines.Add(line);
                continue;
            }

            if (trimmed.StartsWith("```")) { inCode = true; continue; }

            if (string.IsNullOrWhiteSpace(line)) { FlushList(); continue; }

            if (trimmed.StartsWith("# "))
            {
                FlushList();
                var p = new Paragraph { FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) };
                p.SetResourceReference(Paragraph.ForegroundProperty, "PrimaryForeground");
                foreach (var il in ParseInline(trimmed.Substring(2))) p.Inlines.Add(il);
                doc.Blocks.Add(p);
                continue;
            }

            if (trimmed.StartsWith("## "))
            {
                FlushList();
                var p = new Paragraph { FontSize = 15, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 3) };
                p.SetResourceReference(Paragraph.ForegroundProperty, "PrimaryForeground");
                foreach (var il in ParseInline(trimmed.Substring(3))) p.Inlines.Add(il);
                doc.Blocks.Add(p);
                continue;
            }

            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                listItems.Add((trimmed.Substring(2).Trim(), false));
                continue;
            }

            var m = Regex.Match(trimmed, @"^\d+[\.\)]\s+(.*)$");
            if (m.Success)
            {
                listItems.Add((m.Groups[1].Value, true));
                continue;
            }

            FlushList();
            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            para.SetResourceReference(Paragraph.ForegroundProperty, "PrimaryForeground");
            foreach (var il in ParseInline(trimmed)) para.Inlines.Add(il);
            doc.Blocks.Add(para);
        }

        FlushList();
        FlushCode();
    }

    private static IEnumerable<Inline> ParseInline(string s)
    {
        var inlines = new List<Inline>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    var r = new Run(s.Substring(i + 1, end - i - 1))
                    {
                        FontFamily = new FontFamily("Consolas, Menlo, Monaco, Courier New, monospace")
                    };
                    r.SetResourceReference(Run.ForegroundProperty, "AccentBrush");
                    inlines.Add(r);
                    i = end + 1;
                    continue;
                }
            }

            if (s[i] == '*')
            {
                if (i + 1 < s.Length && s[i + 1] == '*')            // 加粗 **x**
                {
                    int end = s.IndexOf("**", i + 2);
                    if (end >= 0)
                    {
                        inlines.Add(new Bold(new Run(s.Substring(i + 2, end - i - 2))));
                        i = end + 2;
                        continue;
                    }
                }
                else                                            // 斜体 *x*
                {
                    int end = s.IndexOf('*', i + 1);
                    if (end > i + 1)
                    {
                        inlines.Add(new Italic(new Run(s.Substring(i + 1, end - i - 1))));
                        i = end + 1;
                        continue;
                    }
                }
            }

            int next = s.IndexOfAny(new[] { '`', '*' }, i);
            if (next < 0)
            {
                inlines.Add(new Run(s.Substring(i)));
                break;
            }
            inlines.Add(new Run(s.Substring(i, next - i)));
            i = next;
        }
        return inlines;
    }
}
