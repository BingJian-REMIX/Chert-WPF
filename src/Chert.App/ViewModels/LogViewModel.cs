using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
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
                ApplyFilter();
        }
    }

    public bool OnlyErrors
    {
        get => _onlyErrors;
        set
        {
            if (SetField(ref _onlyErrors, value))
                ApplyFilter();
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
        var text = LogManager.ReadLog(SelectedFile.FullPath);
        var all = LogManager.ParseLines(text);
        Lines = new ObservableCollection<LogLine>(LogManager.Filter(all, Keyword, OnlyErrors));
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

    private void LoadSelected()
    {
        if (SelectedFile is null)
        {
            Lines = new();
            ChatEntries = new();
            HasChat = false;
            ChatCount = 0;          // 同步条数，供开关旁的「（无）」显示
            ChatStatus = "未选择文件";
            return;
        }
        var text = LogManager.ReadLog(SelectedFile.FullPath);
        var all = LogManager.ParseLines(text);
        Lines = new ObservableCollection<LogLine>(LogManager.Filter(all, Keyword, OnlyErrors));

        // P11：聊天记录与日志正文同源，一次读取同时提取
        var chat = ChatExtractor.Extract(text);
        ChatEntries = new ObservableCollection<ChatEntry>(chat);
        HasChat = chat.Count > 0;
        ChatCount = chat.Count;   // 同步条数
        ChatStatus = chat.Count > 0
            ? $"从 {SelectedFile.Name} 提取到 {chat.Count} 条聊天记录。"
            : $"未在 {SelectedFile.Name} 中找到聊天记录。";
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

    private Task LoadSelectedAsync()
    {
        LoadSelected();
        return Task.CompletedTask;
    }
}
