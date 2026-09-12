using System.Collections.ObjectModel;
using System.Windows.Input;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>
/// 一条命令语法参考项。
/// </summary>
public sealed record CommandSyntax(string Name, string Syntax);

/// <summary>
/// 工具箱 → 命令语法表（对齐 MCLCS-Linux CommandView）：列出常用指令语法供查阅，
/// 并提供拼接 / 复制入口，便于快速组装复杂命令。
/// </summary>
public class CommandViewModel : ObservableObject
{
    public ObservableCollection<CommandSyntax> Reference { get; } = new()
    {
        new("给予物品", "/give <玩家> <物品> [数量]"),
        new("传送", "/tp <目标> <x> <y> <z>"),
        new("填充", "/fill <x1> <y1> <z1> <x2> <y2> <z2> <方块> [模式]"),
        new("召唤实体", "/summon <实体> [x] [y] [z] [数据标签]"),
        new("设置时间", "/time set <day|night|数值>"),
        new("游戏难度", "/difficulty <peaceful|easy|normal|hard>"),
        new("天气", "/weather <clear|rain|thunder> [时长]"),
        new("游戏模式", "/gamemode <survival|creative|adventure|spectator> [玩家]"),
        new("给予经验", "/xp <数量>L [玩家]"),
        new("执行", "/execute <条件> -> <命令>"),
    };

    private string _composed = "";
    public string Composed { get => _composed; set => SetField(ref _composed, value); }

    private string _status = "就绪";
    public string Status { get => _status; set => SetField(ref _status, value); }

    /// <summary>把某条语法模板追加到拼接框（参数占位保持原样，便于手动替换）。</summary>
    public ICommand InsertCommand => new RelayCommand(p => Insert(p as string));

    /// <summary>把拼接框内容复制到系统剪贴板。</summary>
    public ICommand CopyCommand => new RelayCommand(_ => Copy());

    private void Insert(string? syntax)
    {
        if (string.IsNullOrWhiteSpace(syntax)) return;
        Composed = string.IsNullOrWhiteSpace(Composed) ? syntax! : Composed + "\n" + syntax;
        Status = "已插入语法模板";
    }

    private void Copy()
    {
        if (string.IsNullOrWhiteSpace(Composed))
        {
            Status = "无可复制内容";
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(Composed);
            Status = "已复制到剪贴板";
        }
        catch (Exception ex)
        {
            Status = $"复制失败：{ex.Message}";
        }
    }
}
