using System;
using System.Collections.Generic;
using System.Text;

namespace Chert.Core.Toolbox;

/// <summary>一条聊天记录（从游戏日志中提取）。</summary>
public class ChatEntry
{
    /// <summary>原始整行文本（保留时间戳前缀，便于回溯上下文）。</summary>
    public string Raw { get; init; } = "";

    /// <summary>时间戳（形如 12:34:56），无则为空。</summary>
    public string Time { get; init; } = "";

    /// <summary>发言玩家名。</summary>
    public string Player { get; init; } = "";

    /// <summary>聊天正文。</summary>
    public string Message { get; init; } = "";

    /// <summary>该条是否由玩家自己发出（本地单人游戏时把 <c>&lt;me&gt;</c> 之外都算他人）。</summary>
    public bool IsLocal { get; init; }
}

/// <summary>
/// 聊天记录提取器：从 Minecraft 日志中抽取聊天行。
/// <para>需兼容两种历史格式：</para>
/// <list type="bullet">
///   <item>1.7+ 正式版：<c>[12:34:56] [CHAT] &lt;玩家&gt; 内容</c>（部分版本有 <c>[Render thread/CHAT]</c>）</item>
///   <item>1.6 及更早：<c>[12:34:56] &lt;玩家&gt; 内容</c>（无 CHAT 标记）</item>
/// </list>
/// </summary>
public static class ChatExtractor
{
    /// <summary>从整份日志文本中提取聊天行（按出现顺序）。</summary>
    /// <param name="logText">日志全文（可含 \r\n / \n 混合）。</param>
    /// <param name="maxCount">最多返回多少条，0 或负数表示不限。</param>
    public static List<ChatEntry> Extract(string? logText, int maxCount = 0)
    {
        var result = new List<ChatEntry>();
        if (string.IsNullOrEmpty(logText)) return result;

        foreach (var raw in logText.Replace("\r\n", "\n").Split('\n'))
        {
            if (TryParseLine(raw, out var entry)) result.Add(entry!);
            if (maxCount > 0 && result.Count >= maxCount) break;
        }
        return result;
    }

    /// <summary>尝试把一行解析为聊天记录；不是聊天行返回 false。</summary>
    public static bool TryParseLine(string? line, out ChatEntry? entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var rest = line!.Trim();

        // 剥掉开头的时间戳 [12:34:56]
        var time = "";
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']');
            if (close > 1)
            {
                var head = rest[1..close];
                // 形如 12:34:56 或 12:34:56.789
                if (head.Length is >= 8 and <= 12 && head.Contains(':') && !head.Contains(' '))
                {
                    time = head;
                    rest = rest[(close + 1)..].TrimStart();
                }
            }
        }
        if (rest.Length == 0) return false;

        // 去掉日志级别 / 线程标记：[CHAT]、[Render thread/CHAT]、[Server thread/CHAT]、INFO] 等
        if (rest.StartsWith('['))
        {
            var close = rest.IndexOf(']');
            if (close > 0)
            {
                var tag = rest[1..close];
                if (tag.Contains("CHAT", StringComparison.OrdinalIgnoreCase)
                    || tag.Equals("INFO", StringComparison.OrdinalIgnoreCase)
                    || tag.Contains("thread", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest[(close + 1)..].TrimStart();
                }
            }
        }
        if (rest.Length == 0) return false;

        // 聊天正文必须是 <玩家> 内容
        if (rest.Length < 3 || rest[0] != '<') return false;
        var gt = rest.IndexOf('>');
        if (gt <= 1) return false;

        var player = rest[1..gt];
        if (player.Length == 0 || player.Contains('\n')) return false;

        // 玩家名不应含空白（Minecraft 名字不允许），据此排除误判
        foreach (var c in player)
        {
            if (char.IsWhiteSpace(c)) return false;
        }

        var message = rest[(gt + 1)..].Trim();
        entry = new ChatEntry
        {
            Raw = line!,
            Time = time,
            Player = player,
            Message = message,
            IsLocal = player.Equals("<me>", StringComparison.OrdinalIgnoreCase) || player == "me"
        };
        return true;
    }

    /// <summary>导出为纯文本（每行 "[时间] 玩家: 内容"）。</summary>
    public static string ToPlainText(IEnumerable<ChatEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            if (e.Time.Length > 0) sb.Append('[').Append(e.Time).Append("] ");
            sb.Append(e.Player).Append(": ").AppendLine(e.Message);
        }
        return sb.ToString();
    }

    /// <summary>导出为 Markdown（表格，GFM 风格）。</summary>
    public static string ToMarkdown(IEnumerable<ChatEntry> entries)
    {
        var list = new List<ChatEntry>(entries);
        var sb = new StringBuilder();
        sb.AppendLine("# 聊天记录");
        sb.AppendLine();
        sb.AppendLine($"共 {list.Count} 条");
        sb.AppendLine();
        sb.AppendLine("| 时间 | 玩家 | 内容 |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var e in list)
        {
            sb.Append('|').Append(Escape(e.Time))
              .Append('|').Append(Escape(e.Player))
              .Append('|').Append(Escape(e.Message))
              .AppendLine("|");
        }
        return sb.ToString();

        static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
    }
}
