using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Chert.Core.Theme;

namespace Chert.App.Themes;

/// <summary>
/// PNG UI 图标控件：从程序集内嵌资源（Resources/Icons/{light|dark}/{token}.png）加载图标，
/// 完全取代原矢量 Geometry 图标。亮/暗随 <see cref="ThemeManager.Current"/> 自动切换；
/// token 缺失或加载失败时显示空白（不回退到任何矢量图形）。
/// <para>用法：&lt;themes:PngIcon Token="minimize" Size="12" /&gt;</para>
/// </summary>
public sealed class PngIcon : Image
{
    public static readonly DependencyProperty TokenProperty =
        DependencyProperty.Register(nameof(Token), typeof(string), typeof(PngIcon),
            new PropertyMetadata(null, OnPropChanged));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(int), typeof(PngIcon),
            new PropertyMetadata(16, OnPropChanged));

    /// <summary>图标 token（与内嵌 PNG 文件名、SidebarModel / DownloadCardItem 的键名一致）。</summary>
    public string? Token
    {
        get => (string?)GetValue(TokenProperty);
        set => SetValue(TokenProperty, value);
    }

    /// <summary>渲染尺寸（正方形，单位 px）。</summary>
    public int Size
    {
        get => (int)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public PngIcon()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Stretch = Stretch.Uniform;
        ThemeManager.OnThemeChanged += OnThemeChanged;
        IconManager.HighDpiChanged += OnHighDpiChanged;
        Reload();
    }

    private void OnThemeChanged(ThemeType _) => Reload();

    private void OnHighDpiChanged() => Reload();

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs _) =>
        ((PngIcon)d).Reload();

    private void Reload()
    {
        Source = IconImage.Get(Token, Size);
        Width = Size;
        Height = Size;
    }
}
