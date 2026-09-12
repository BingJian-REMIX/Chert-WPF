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
    }
}
