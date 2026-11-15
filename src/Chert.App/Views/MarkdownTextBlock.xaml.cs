using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Chert.App.Views;

/// <summary>将 Markdown 字符串渲染为只读 FlowDocument 的轻量控件，供助手气泡使用。</summary>
public partial class MarkdownTextBlock : UserControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(nameof(Markdown), typeof(string), typeof(MarkdownTextBlock),
            new PropertyMetadata(null, OnMarkdownChanged));

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownTextBlock()
    {
        InitializeComponent();
        SizeChanged += (_, _) => SyncPageWidth();
    }

    /// <summary>把 FlowDocument 的列宽钉到 RichTextBox 的实际可用宽度。</summary>
    /// <remarks>宽度不受限的父容器（水平 StackPanel / ScrollViewer）里，RichTextBox 测量时
    /// FlowDocument 会塌缩到最小列宽，中文逐字换行成竖条。只在宽度变化超过 1px 时写回，
    /// 避免 SizeChanged 与重排互相触发形成循环。</remarks>
    private void SyncPageWidth()
    {
        var doc = Rtb.Document;
        if (doc is null) return;
        var w = Rtb.ActualWidth;
        if (w <= 1) w = ActualWidth;
        if (w <= 1) return;
        if (!double.IsNaN(doc.PageWidth) && Math.Abs(doc.PageWidth - w) < 1) return;
        doc.PageWidth = w;
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((MarkdownTextBlock)d).Render(e.NewValue as string);
    }

    private void Render(string? md)
    {
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "PrimaryForeground");
        try
        {
            MarkdownParser.Fill(doc, md ?? "");
        }
        catch
        {
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph(new Run(md ?? "")));
        }
        Rtb.Document = doc;
        SyncPageWidth();
    }
}
