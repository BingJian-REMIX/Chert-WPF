using System.Windows;
using System.Windows.Controls;

namespace Chert.App.Controls;

/// <summary>
/// 模态弹窗基类（需求规格 1.4：模态居中、半透明遮罩、主操作右置）。
/// 自身只承载内容与开关状态；遮罩与居中布局由宿主页用
/// Themes/Controls.xaml 的 ModalOverlayStyle / ModalCardStyle 提供。
/// <para>
/// 典型用法：
/// <code>
/// &lt;Grid Style="{StaticResource ModalOverlayStyle}" Visibility="{Binding IsOpen, ...}"&gt;
///     &lt;Border Style="{StaticResource ModalCardStyle}"&gt;
///         &lt;local:MyDialog .../&gt;
///     &lt;/Border&gt;
/// &lt;/Grid&gt;
/// </code>
/// </para>
/// </summary>
public class ModalDialog : ContentControl
{
    /// <summary>是否显示。</summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(ModalDialog),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>标题。</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title), typeof(string), typeof(ModalDialog),
            new PropertyMetadata(null));

    /// <summary>是否允许点击遮罩关闭（默认 false）。</summary>
    public static readonly DependencyProperty DismissOnScrimClickProperty =
        DependencyProperty.Register(
            nameof(DismissOnScrimClick), typeof(bool), typeof(ModalDialog),
            new PropertyMetadata(false));

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool DismissOnScrimClick
    {
        get => (bool)GetValue(DismissOnScrimClickProperty);
        set => SetValue(DismissOnScrimClickProperty, value);
    }

    /// <summary>显示时触发（IsOpen 由 false→true）。</summary>
    public event RoutedEventHandler? Opened;

    /// <summary>关闭时触发（IsOpen 由 true→false）。</summary>
    public event RoutedEventHandler? Closed;

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ModalDialog)d;
        if (e.NewValue is true) self.Opened?.Invoke(self, new RoutedEventArgs());
        else self.Closed?.Invoke(self, new RoutedEventArgs());
    }

    /// <summary>显示弹窗（置 IsOpen=true）。</summary>
    public void Show() => IsOpen = true;

    /// <summary>关闭弹窗（置 IsOpen=false）。</summary>
    public void Hide() => IsOpen = false;
}
