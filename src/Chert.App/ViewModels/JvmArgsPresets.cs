using System.Collections.Generic;
using System.Collections.ObjectModel;
using Chert.Core.Localization;
using Chert.Core.Mvvm;

namespace Chert.App.ViewModels;

/// <summary>
/// 清单 #68：JVM 启动参数可视化编辑器的数据模型。
/// 参数分两类——开关型（勾选后追加固定参数）与取值型（勾选后追加「前缀+数值」），
/// 其余无法识别的参数保留在「自定义参数」文本框里，三者共同拼出最终的 JVM 参数列表。
/// </summary>
public class JvmPresetItem : ObservableObject
{
    /// <summary>开关型：勾选即追加 <see cref="Arg"/>。</summary>
    public bool IsFlag { get; init; }

    /// <summary>开关型参数的完整文本（如 -XX:+UseG1GC）。</summary>
    public string Arg { get; init; } = "";

    /// <summary>取值型参数的前缀（如 -XX:MaxGCPauseMillis=）。</summary>
    public string Prefix { get; init; } = "";

    /// <summary>取值型参数的默认数值。</summary>
    public string DefaultValue { get; init; } = "";

    /// <summary>本地化名称的 key。</summary>
    public string NameKey { get; init; } = "";

    /// <summary>本地化说明的 key。</summary>
    public string TipKey { get; init; } = "";

    public string Name => LocaleManager.T(NameKey);
    public string Tip => LocaleManager.T(TipKey);

    /// <summary>取值型才显示数值输入框。</summary>
    public bool HasValue => !IsFlag;

    private bool _isEnabled;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetField(ref _isEnabled, value)) return;
            OnPropertyChanged(nameof(Line));
        }
    }

    private string _value = "";
    public string Value
    {
        get => _value;
        set
        {
            if (!SetField(ref _value, value)) return;
            OnPropertyChanged(nameof(Line));
        }
    }

    /// <summary>本项拼出的完整参数行；未勾选或数值为空时返回 null。</summary>
    public string? Line
    {
        get
        {
            if (!_isEnabled) return null;
            if (IsFlag) return Arg;
            var v = string.IsNullOrWhiteSpace(_value) ? DefaultValue : _value.Trim();
            return string.IsNullOrWhiteSpace(v) ? null : Prefix + v;
        }
    }

    /// <summary>解析文本时静默设置勾选态（不触发 Line 重算之外的额外回写）。</summary>
    public void SetEnabledSilently(bool value) => _isEnabled = value;

    /// <summary>解析文本时静默设置数值。</summary>
    public void SetValueSilently(string value) => _value = value;

    /// <summary>批量解析结束后统一通知界面刷新。</summary>
    public void NotifyAll()
    {
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Line));
    }

    public void Reset()
    {
        _isEnabled = false;
        _value = DefaultValue;
        NotifyAll();
    }
}

/// <summary>内置 JVM 参数预设（顺序即界面显示顺序）。</summary>
public static class JvmArgsPresets
{
    public static ObservableCollection<JvmPresetItem> Create()
    {
        var list = new ObservableCollection<JvmPresetItem>();
        foreach (var d in Definitions)
        {
            list.Add(new JvmPresetItem
            {
                IsFlag = d.IsFlag,
                Arg = d.Arg,
                Prefix = d.Prefix,
                DefaultValue = d.DefaultValue,
                NameKey = d.NameKey,
                TipKey = d.TipKey,
                Value = d.DefaultValue
            });
        }
        return list;
    }

    private record Definition(bool IsFlag, string Arg, string Prefix, string DefaultValue, string NameKey, string TipKey);

    private static readonly Definition[] Definitions =
    {
        // ---- 开关型 ----
        new(true, "-XX:+UseG1GC", "", "", "version.jvm_g1gc", "version.jvm_g1gc_tip"),
        new(true, "-XX:+UseStringDeduplication", "", "", "version.jvm_strdedup", "version.jvm_strdedup_tip"),
        new(true, "-XX:+DisableExplicitGC", "", "", "version.jvm_dis_explicit", "version.jvm_dis_explicit_tip"),
        new(true, "-XX:+AlwaysPreTouch", "", "", "version.jvm_pretouch", "version.jvm_pretouch_tip"),
        new(true, "-XX:+ParallelRefProcEnabled", "", "", "version.jvm_parref", "version.jvm_parref_tip"),
        new(true, "-Dlog4j2.formatMsgNoLookups=true", "", "", "version.jvm_log4j", "version.jvm_log4j_tip"),
        new(true, "-Dfml.ignoreInvalidMinecraftCertificates=true", "", "", "version.jvm_fml_cert", "version.jvm_fml_cert_tip"),
        new(true, "-Dfml.ignorePatchDiscrepancies=true", "", "", "version.jvm_fml_patch", "version.jvm_fml_patch_tip"),
        new(true, "-Dfile.encoding=UTF-8", "", "", "version.jvm_encoding", "version.jvm_encoding_tip"),

        // ---- 取值型 ----
        new(false, "", "-XX:MaxGCPauseMillis=", "50", "version.jvm_maxpause", "version.jvm_maxpause_tip"),
        new(false, "", "-XX:ParallelGCThreads=", "4", "version.jvm_gcthreads", "version.jvm_gcthreads_tip"),
        new(false, "", "-XX:InitiatingHeapOccupancyPercent=", "45", "version.jvm_ihop", "version.jvm_ihop_tip"),
    };

    /// <summary>尝试用一行参数匹配某个预设；命中返回该预设，否则返回 null。</summary>
    public static JvmPresetItem? Match(IEnumerable<JvmPresetItem> presets, string line)
    {
        foreach (var p in presets)
        {
            if (p.IsFlag)
            {
                if (string.Equals(line, p.Arg, System.StringComparison.Ordinal)) return p;
            }
            else if (line.StartsWith(p.Prefix, System.StringComparison.Ordinal))
            {
                return p;
            }
        }
        return null;
    }
}
