using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>日志管理面板：列出游戏日志/崩溃报告，读取、过滤、导出。</summary>
public class LogViewModel : ObservableObject
{
    private ObservableCollection<LogFileInfo> _files = new();
    private LogFileInfo? _selectedFile;
    private ObservableCollection<LogLine> _lines = new();
    private string _keyword = "";
    private bool _onlyErrors;
    private string _statusMessage = "";

    private List<LogLine> _allLines = new();          // 已解析的全部行（内存缓存，过滤不再读盘）
    private readonly DispatcherTimer _filterTimer;
    private int _loadToken;                            // 防止快速切换文件时旧加载覆盖新结果
    private const int MaxDisplayLines = 50000;         // 单次最多绑定给界面的行数

    // ===== P11：聊天记录（与崩溃报告共用 ChatExtractor）=====

    private ObservableCollection<ChatEntry> _chatEntries = new();
    private bool _hasChat;
    private bool _showChatPanel;
    private int _chatCount;
    private string _chatStatus = "未选择文件";

    /// <summary>当前选中日志里提取到的聊天记录（绿色染色展示）。</summary>
    public ObservableCollection<ChatEntry> ChatEntries
    {
        get => _chatEntries;
        set => SetField(ref _chatEntries, value);
    }

    /// <summary>是否提取到聊天记录。</summary>
    public bool HasChat
    {
        get => _hasChat;
        set
        {
            if (SetField(ref _hasChat, value)) ExportChatCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>是否展开聊天记录区（用户要求：改为与「只显示错误」同款的开关式，而非页签）。</summary>
    public bool ShowChatPanel
    {
        get => _showChatPanel;
        set => SetField(ref _showChatPanel, value);
    }

    /// <summary>聊天条数文本（开关旁显示，让用户不必展开就知道有没有内容）。</summary>
    public string ChatCountText => _chatCount > 0 ? $"（{_chatCount} 条）" : "（无）";

    /// <summary>聊天条数。★ 需在 <see cref="ChatEntries"/> 变化时通知 <see cref="ChatCountText"/>。</summary>
    private int ChatCount
    {
        get => _chatCount;
        set
        {
            if (_chatCount == value) return;
            _chatCount = value;
            OnPropertyChanged(nameof(ChatCountText));
        }
    }

    /// <summary>聊天区状态文案。</summary>
    public string ChatStatus
    {
        get => _chatStatus;
        set => SetField(ref _chatStatus, value);
    }

    /// <summary>导出聊天记录（txt / md）。</summary>
    // 类型必须是 RelayCommand（HasChat 变化时要调 RaiseCanExecuteChanged 让导出按钮启用/禁用）
    public RelayCommand ExportChatCommand { get; }

    public ObservableCollection<LogFileInfo> Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }

    public LogFileInfo? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
                _ = LoadSelectedAsync();
        }
    }

    public ObservableCollection<LogLine> Lines
    {
        get => _lines;
        set => SetField(ref _lines, value);
    }

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetField(ref _keyword, value))
            {
                _filterTimer.Stop();
                _filterTimer.Start();   // 去抖：停止输入 250ms 后再过滤，避免逐字符重读磁盘
            }
        }
    }

    public bool OnlyErrors
    {
        get => _onlyErrors;
        set
        {
            if (SetField(ref _onlyErrors, value))
            {
                _filterTimer.Stop();
                _filterTimer.Start();   // 去抖：切换开关后统一过滤
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }

    public LogViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        ExportCommand = new RelayCommand(_ => Export());
        ExportChatCommand = new RelayCommand(p => ExportChat(p as string ?? "txt"), _ => HasChat);
        _filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _filterTimer.Tick += (_, _) => { _filterTimer.Stop(); ApplyFilter(); };
        Refresh();
    }

    public void Refresh()
    {
        var root = LauncherService.Instance.GameRoot;
        Files = new ObservableCollection<LogFileInfo>(LogManager.ListLogs(root));
        StatusMessage = $"共 {Files.Count} 个日志/崩溃报告文件";
    }

    private void ApplyFilter()
    {
        if (SelectedFile is null) return;
        var filtered = LogManager.Filter(_allLines, Keyword, OnlyErrors, MaxDisplayLines, out int total);
        Lines = new ObservableCollection<LogLine>(filtered);
        var isFiltering = !string.IsNullOrWhiteSpace(Keyword) || OnlyErrors;
        StatusMessage = filtered.Count < total
            ? (isFiltering
                ? $"匹配 {total} 行，已显示最近 {filtered.Count} 行（文件共 {_allLines.Count} 行）"
                : $"已显示最近 {filtered.Count} 行（文件共 {_allLines.Count} 行）")
            : (isFiltering
                ? $"匹配 {total} 行（文件共 {_allLines.Count} 行）"
                : $"共 {_allLines.Count} 行");
    }

    private void Export()
    {
        if (SelectedFile is null) { StatusMessage = "请先选择一个文件"; return; }
        var dest = UIService.PickFolder("选择导出目录");
        if (string.IsNullOrEmpty(dest)) return;
        var target = Path.Combine(dest, SelectedFile.Name);
        var ok = LogManager.Export(SelectedFile.FullPath, target);
        StatusMessage = ok ? $"已导出到 {target}" : "导出失败";
    }

    private async Task LoadSelectedAsync()
    {
        var file = SelectedFile;
        if (file is null)
        {
            _allLines.Clear();
            Lines = new();
            ChatEntries = new();
            HasChat = false;
            ChatCount = 0;
            ChatStatus = "未选择文件";
            StatusMessage = "";
            return;
        }
        var token = ++_loadToken;
        StatusMessage = $"读取中：{file.Name}";
        string text = "";
        List<LogLine> all = new();
        List<ChatEntry> chat = new();
        await Task.Run(() =>
        {
            text = LogManager.ReadLog(file.FullPath);
            all = LogManager.ParseLines(text);
            chat = ChatExtractor.Extract(text);
        });
        if (token != _loadToken) return;   // 已被更新的选择覆盖，丢弃陈旧结果
        _allLines = all;
        ChatEntries = new ObservableCollection<ChatEntry>(chat);
        HasChat = chat.Count > 0;
        ChatCount = chat.Count;
        ChatStatus = HasChat
            ? $"从 {file.Name} 提取到 {chat.Count} 条聊天记录。"
            : $"未在 {file.Name} 中找到聊天记录。";
        ApplyFilter();
        var shown = Lines.Count;
        StatusMessage = shown < _allLines.Count
            // 被截断了就如实说，别让用户以为看到的就是全部
            ? $"已载入 {file.Name}：共 {_allLines.Count} 行，界面只显示最近 {shown} 行"
            : $"已载入 {file.Name}（{_allLines.Count} 行）";
    }

    /// <summary>P11：把聊天记录导出为 txt / md。</summary>
    private void ExportChat(string format)
    {
        if (!HasChat || ChatEntries.Count == 0) return;
        try
        {
            var isMd = format.Equals("md", StringComparison.OrdinalIgnoreCase);
            var baseName = Path.GetFileNameWithoutExtension(SelectedFile?.Name ?? "chat");
            var dest = UIService.PickFolder("选择导出目录");
            if (string.IsNullOrEmpty(dest)) return;

            var ext = isMd ? "md" : "txt";          // 预取，避免在插值串里放带引号的三元
            var path = Path.Combine(dest, $"{baseName}-chat.{ext}");
            File.WriteAllText(path, isMd
                ? ChatExtractor.ToMarkdown(ChatEntries)
                : ChatExtractor.ToPlainText(ChatEntries));
            StatusMessage = $"聊天记录已导出到 {path}";
            ToastService.Show("聊天记录已导出", path);
        }
        catch (Exception ex)
        {
            StatusMessage = "导出失败：" + ex.Message;
            ToastService.Show("导出失败", ex.Message, ToastKind.Error);
        }
    }

    // 旧 LoadSelected/LoadSelectedAsync 已合并为上方异步版本
}
