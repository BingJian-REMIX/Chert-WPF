using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>命令速查表的一行。</summary>
public class CommandEntry
{
    public string Name { get; init; } = "";
    public string Syntax { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public bool MatchesFilter { get; set; } = true;
}

/// <summary>
/// 开发工具面板（规格 2.3 面板 11）：Mod 开发环境骨架生成、资源包/数据包创建器、命令语法表。
/// </summary>
public class DevToolsViewModel : ObservableObject
{
    private string _mode = "commands"; // commands | mod | resourcepack
    private string _modName = "";
    private string _modVersion = "1.0.0";
    private string _modLoader = "fabric";
    private string _modGameVersion = "1.21";
    private string _modPackage = "";
    private string _rpName = "";
    private string _rpFormat = "34";
    private string _commandFilter = "";
    private string _statusMessage = "";

    public string Mode { get => _mode; set { SetField(ref _mode, value); OnPropertyChanged(nameof(ShowCommands)); OnPropertyChanged(nameof(ShowMod)); OnPropertyChanged(nameof(ShowResourcePack)); } }
    public bool ShowCommands => Mode == "commands";
    public bool ShowMod => Mode == "mod";
    public bool ShowResourcePack => Mode == "resourcepack";

    public string ModName { get => _modName; set => SetField(ref _modName, value); }
    public string ModVersion { get => _modVersion; set => SetField(ref _modVersion, value); }
    public string ModLoader { get => _modLoader; set => SetField(ref _modLoader, value); }
    public string ModGameVersion { get => _modGameVersion; set => SetField(ref _modGameVersion, value); }
    public string ModPackage { get => _modPackage; set => SetField(ref _modPackage, value); }
    public string RpName { get => _rpName; set => SetField(ref _rpName, value); }
    public string RpFormat { get => _rpFormat; set => SetField(ref _rpFormat, value); }
    public string CommandFilter { get => _commandFilter; set { SetField(ref _commandFilter, value); ApplyFilter(); } }
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public ObservableCollection<CommandEntry> Commands { get; } = new();

    public ICommand GenerateModCommand { get; }
    public ICommand GenerateRpCommand { get; }

    public DevToolsViewModel()
    {
        Mode = "commands";
        GenerateModCommand = new RelayCommand(_ => GenerateMod());
        GenerateRpCommand = new RelayCommand(_ => GenerateResourcePack());
        BuildCommandTable();
    }

    private void BuildCommandTable()
    {
        var entries = new[]
        {
            new CommandEntry { Name = "give", Syntax = "/give <player> <item> [count]", Category = "物品", Description = "给予玩家指定物品" },
            new CommandEntry { Name = "tp", Syntax = "/tp <target> [dst]", Category = "传送", Description = "传送实体" },
            new CommandEntry { Name = "gamemode", Syntax = "/gamemode <mode> [player]", Category = "游戏", Description = "切换游戏模式（survival/creative/adventure/spectator）" },
            new CommandEntry { Name = "time", Syntax = "/time set <value>", Category = "世界", Description = "设置游戏时间（0-24000 或 day/night/noon/midnight）" },
            new CommandEntry { Name = "weather", Syntax = "/weather <type> [duration]", Category = "世界", Description = "设置天气（clear/rain/thunder）" },
            new CommandEntry { Name = "kill", Syntax = "/kill [targets]", Category = "实体", Description = "杀死实体" },
            new CommandEntry { Name = "summon", Syntax = "/summon <entity> [pos] [nbt]", Category = "实体", Description = "召唤实体" },
            new CommandEntry { Name = "effect", Syntax = "/effect give <targets> <effect> [seconds] [amplifier] [hideParticles]", Category = "实体", Description = "给予状态效果" },
            new CommandEntry { Name = "fill", Syntax = "/fill <from> <to> <block> [mode]", Category = "方块", Description = "填充方块区域" },
            new CommandEntry { Name = "setblock", Syntax = "/setblock <pos> <block> [mode]", Category = "方块", Description = "放置方块" },
            new CommandEntry { Name = "clone", Syntax = "/clone <from> <to> <dst> [mask] [mode]", Category = "方块", Description = "复制方块区域" },
            new CommandEntry { Name = "execute", Syntax = "/execute as <target> run <command>", Category = "高级", Description = "以其他实体身份执行命令" },
            new CommandEntry { Name = "execute at", Syntax = "/execute at <target> run <command>", Category = "高级", Description = "在目标位置执行命令" },
            new CommandEntry { Name = "execute if/unless", Syntax = "/execute if block <pos> <block> run <command>", Category = "高级", Description = "条件执行" },
            new CommandEntry { Name = "scoreboard", Syntax = "/scoreboard objectives add <name> <criteria> [displayName]", Category = "记分板", Description = "创建记分项" },
            new CommandEntry { Name = "tag", Syntax = "/tag <targets> add <name>", Category = "实体", Description = "给实体添加标签" },
            new CommandEntry { Name = "team", Syntax = "/team add <team> [displayName]", Category = "队伍", Description = "创建队伍" },
            new CommandEntry { Name = "bossbar", Syntax = "/bossbar add <id> <name>", Category = "UI", Description = "创建 Boss 血条" },
            new CommandEntry { Name = "title", Syntax = "/title <player> title <text>", Category = "UI", Description = "显示标题文字" },
            new CommandEntry { Name = "clear", Syntax = "/clear [player] [item]", Category = "物品", Description = "清空物品栏" },
            new CommandEntry { Name = "enchant", Syntax = "/enchant <player> <enchantment> [level]", Category = "物品", Description = "给物品附魔" },
            new CommandEntry { Name = "difficulty", Syntax = "/difficulty <level>", Category = "游戏", Description = "设置难度" },
            new CommandEntry { Name = "gamerule", Syntax = "/gamerule <rule> [value]", Category = "游戏", Description = "设置游戏规则" },
            new CommandEntry { Name = "seed", Syntax = "/seed", Category = "世界", Description = "显示世界种子" },
            new CommandEntry { Name = "locate", Syntax = "/locate structure <structure>", Category = "世界", Description = "定位最近的指定结构" },
            new CommandEntry { Name = "spawnpoint", Syntax = "/spawnpoint [player] [pos] [angle]", Category = "实体", Description = "设置重生点" },
            new CommandEntry { Name = "xp", Syntax = "/xp <amount> [player]", Category = "实体", Description = "给予经验" },
            new CommandEntry { Name = "advancement", Syntax = "/advancement grant <player> <advancement>", Category = "成就", Description = "授予进度" },
            new CommandEntry { Name = "recipe", Syntax = "/recipe give <player> <recipe>", Category = "物品", Description = "解锁合成配方" },
            new CommandEntry { Name = "spreadplayers", Syntax = "/spreadplayers <center> <distance> <range> <targets>", Category = "传送", Description = "随机散布目标" },
            new CommandEntry { Name = "tellraw", Syntax = "/tellraw <targets> <raw json>", Category = "聊天", Description = "发送 JSON 格式消息" },
            new CommandEntry { Name = "playsound", Syntax = "/playsound <sound> <source> <targets> [pos] [volume] [pitch]", Category = "音效", Description = "播放音效" },
            new CommandEntry { Name = "stopsound", Syntax = "/stopsound <targets> [source] [sound]", Category = "音效", Description = "停止播放音效" },
            new CommandEntry { Name = "particle", Syntax = "/particle <particle> <pos> [delta] [speed] [count] [mode] [viewers]", Category = "粒子", Description = "创建粒子效果" },
            new CommandEntry { Name = "worldborder", Syntax = "/worldborder set <size> [time]", Category = "世界", Description = "设置世界边界" },
            new CommandEntry { Name = "datapack", Syntax = "/datapack enable <name>", Category = "数据包", Description = "启用数据包" },
            new CommandEntry { Name = "function", Syntax = "/function <name>", Category = "数据包", Description = "执行数据包函数" },
            new CommandEntry { Name = "schedule", Syntax = "/schedule function <fn> <time>", Category = "数据包", Description = "定时执行函数" },
            new CommandEntry { Name = "place", Syntax = "/place feature <feature> [pos]", Category = "方块", Description = "放置地物" },
            new CommandEntry { Name = "return", Syntax = "/return <value>", Category = "函数", Description = "从函数返回" },
        };

        foreach (var e in entries.OrderBy(e => e.Name))
            Commands.Add(e);
    }

    private void ApplyFilter()
    {
        var filter = CommandFilter.Trim().ToLowerInvariant();
        foreach (var cmd in Commands)
            cmd.MatchesFilter = string.IsNullOrEmpty(filter) ||
                cmd.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                cmd.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                cmd.Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void GenerateMod()
    {
        if (string.IsNullOrWhiteSpace(ModName)) { StatusMessage = "请输入 Mod 名称"; return; }
        var pkg = string.IsNullOrWhiteSpace(ModPackage)
            ? $"com.example.{ModName.ToLowerInvariant()}"
            : ModPackage;

        var dir = UIService.PickFolder("选择 Mod 项目输出目录");
        if (string.IsNullOrWhiteSpace(dir)) return;

        try
        {
            var projDir = Path.Combine(dir, ModName);
            Directory.CreateDirectory(projDir);
            Directory.CreateDirectory(Path.Combine(projDir, "src", "main", "java", pkg.Replace('.', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.Combine(projDir, "src", "main", "resources"));

            // build.gradle
            File.WriteAllText(Path.Combine(projDir, "build.gradle"),
                "plugins {\n" +
                "    id 'fabric-loom' version '1.6-SNAPSHOT'\n" +
                "    id 'maven-publish'\n" +
                "}\n" +
                $"group = '{pkg}'\n" +
                $"version = '{ModVersion}'\n\n" +
                "dependencies {\n" +
                $"    minecraft 'com.mojang:minecraft:{ModGameVersion}'\n" +
                "    mappings 'net.fabricmc:yarn:{mc}+build.1'\n" +
                "    modImplementation 'net.fabricmc:fabric-loader:0.15.0'\n" +
                "    modImplementation 'net.fabricmc.fabric-api:fabric-api:0.95.0'\n" +
                "}\n");

            // fabric.mod.json
            File.WriteAllText(Path.Combine(projDir, "src", "main", "resources", "fabric.mod.json"),
                "{\n" +
                $"  \"schemaVersion\": 1,\n" +
                $"  \"id\": \"{ModName.ToLowerInvariant()}\",\n" +
                $"  \"version\": \"{ModVersion}\",\n" +
                $"  \"name\": \"{ModName}\",\n" +
                "  \"entrypoints\": { \"main\": [\"{pkg}.ExampleMod\"] },\n" +
                "}");

            StatusMessage = $"Mod 项目已生成到 {projDir}";
            ToastService.Show("开发工具", $"已生成 {ModName} 项目骨架", ToastKind.Success);
        }
        catch (Exception ex) { StatusMessage = $"生成失败：{ex.Message}"; }
    }

    private void GenerateResourcePack()
    {
        if (string.IsNullOrWhiteSpace(RpName)) { StatusMessage = "请输入资源包名称"; return; }
        var dir = UIService.PickFolder("选择资源包输出目录");
        if (string.IsNullOrWhiteSpace(dir)) return;

        try
        {
            var packDir = Path.Combine(dir, RpName);
            Directory.CreateDirectory(packDir);
            Directory.CreateDirectory(Path.Combine(packDir, "assets", "minecraft", "textures"));
            Directory.CreateDirectory(Path.Combine(packDir, "assets", "minecraft", "models"));
            Directory.CreateDirectory(Path.Combine(packDir, "assets", "minecraft", "sounds"));

            File.WriteAllText(Path.Combine(packDir, "pack.mcmeta"),
                "{\n" +
                "  \"pack\": {\n" +
                $"    \"pack_format\": {RpFormat},\n" +
                $"    \"description\": \"{RpName}\"\n" +
                "  }\n" +
                "}");

            File.WriteAllText(Path.Combine(packDir, "pack.png"), ""); // placeholder
            StatusMessage = $"资源包已生成到 {packDir}";
            ToastService.Show("开发工具", $"已生成 {RpName} 资源包", ToastKind.Success);
        }
        catch (Exception ex) { StatusMessage = $"生成失败：{ex.Message}"; }
    }
}
