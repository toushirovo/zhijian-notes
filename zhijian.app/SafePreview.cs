using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zhijian.Core;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace zhijian.app;

internal static class SafePreview
{
    public static FlowDocument Render(string text, Brush foreground, Brush accent, Action<string> navigate)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 14, Foreground = foreground, PagePadding = new Thickness(20), LineHeight = 25 };
        try { foreach (var b in MarkdownLinks.Parse(text)) doc.Blocks.Add(Block(b, accent, navigate, 0)); }
        catch { doc.Blocks.Clear(); doc.Blocks.Add(new Paragraph(new Run(text))); }
        return doc;
    }
    private static WpfBlock Block(MdBlock b, Brush accent, Action<string> navigate, int depth)
    {
        if (depth > 100) throw new InvalidDataException("Markdown 嵌套过深。");
        if (b is ListBlock list)
        {
            var result = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Padding = new Thickness(22, 0, 0, 0), Margin = new Thickness(0, 5, 0, 12) };
            foreach (var child in list.OfType<ListItemBlock>())
            { var item = new ListItem(); foreach (var inner in child) item.Blocks.Add(Block(inner, accent, navigate, depth + 1)); result.ListItems.Add(item); }
            return result;
        }
        if (b is QuoteBlock quote)
        {
            var section = new Section { BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(0, 10, 0, 14) };
            foreach (var inner in quote) section.Blocks.Add(Block(inner, accent, navigate, depth + 1)); return section;
        }
        if (b is CodeBlock code) return new Paragraph(new Run(code.Lines.ToString())) { FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(22, 100, 150, 130)), Margin = new Thickness(0, 10, 0, 14) };
        if (b is ThematicBreakBlock) return new Paragraph(new Run("────────────")) { Foreground = accent };
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        if (b is HeadingBlock h) { paragraph.FontSize = h.Level switch { 1 => 27, 2 => 22, 3 => 18, _ => 16 }; paragraph.FontWeight = FontWeights.SemiBold; paragraph.Margin = new Thickness(0, 12, 0, 14); }
        if (b is LeafBlock leaf)
        {
            if (leaf.Inline != null) foreach (var inline in leaf.Inline) paragraph.Inlines.Add(Inline(inline, accent, navigate, depth + 1));
            else paragraph.Inlines.Add(new Run(leaf.Lines.ToString()));
        }
        return paragraph;
    }
    private static WpfInline Inline(Markdig.Syntax.Inlines.Inline inline, Brush accent, Action<string> navigate, int depth)
    {
        if (depth > 100) throw new InvalidDataException("Markdown 嵌套过深。");
        if (inline is LiteralInline literal) return new Run(literal.Content.ToString());
        if (inline is CodeInline code) return new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromArgb(22, 100, 150, 130)) };
        if (inline is LineBreakInline) return new LineBreak();
        if (inline is HtmlInline html) return new Run(html.Tag);
        if (inline is AutolinkInline auto) return Link(auto.Url, [new Run(auto.Url)], accent, navigate);
        if (inline is ContainerInline container)
        {
            var children = container.Select(x => Inline(x, accent, navigate, depth + 1)).ToArray();
            if (inline is LinkInline link)
            {
                if (link.IsImage) { var imageText = new Span(new Run("[图片：")); imageText.Inlines.AddRange(children); imageText.Inlines.Add(new Run(" · 未加载]")); return imageText; }
                return Link(link.Url ?? "", children, accent, navigate);
            }
            Span span = inline is EmphasisInline e ? e.DelimiterCount >= 2 ? new Bold() : new Italic() : new Span();
            span.Inlines.AddRange(children); return span;
        }
        return new Run("");
    }
    private static WpfInline Link(string url, WpfInline[] children, Brush accent, Action<string> navigate)
    {
        var valid = MarkdownLinks.Target(url) != null || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
        if (!valid) { var span = new Span(); span.Inlines.AddRange(children); span.ToolTip = "该链接协议或路径不受支持"; return span; }
        var link = new Hyperlink { Foreground = accent, ToolTip = url }; link.Inlines.AddRange(children);
        link.Click += (_, _) => navigate(url); return link;
    }
}
