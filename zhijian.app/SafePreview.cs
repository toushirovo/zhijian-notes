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
    public static FlowDocument Render(string text, Brush foreground, Brush accent, Action<string> navigate, Func<string, byte[]>? imageSource = null)
    {
        var doc = new FlowDocument { FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 14, Foreground = foreground, PagePadding = new Thickness(20), LineHeight = 25 };
        try { foreach (var b in MarkdownLinks.Parse(text)) doc.Blocks.Add(Block(b, accent, navigate, 0, imageSource)); }
        catch { doc.Blocks.Clear(); doc.Blocks.Add(new Paragraph(new Run(text))); }
        return doc;
    }
    private static WpfBlock Block(MdBlock b, Brush accent, Action<string> navigate, int depth, Func<string, byte[]>? imageSource)
    {
        if (depth > 100) throw new InvalidDataException("Markdown 嵌套过深。");
        if (b is ListBlock list)
        {
            var result = new System.Windows.Documents.List { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc, Padding = new Thickness(22, 0, 0, 0), Margin = new Thickness(0, 5, 0, 12) };
            foreach (var child in list.OfType<ListItemBlock>())
            { var item = new ListItem(); foreach (var inner in child) item.Blocks.Add(Block(inner, accent, navigate, depth + 1, imageSource)); result.ListItems.Add(item); }
            return result;
        }
        if (b is QuoteBlock quote)
        {
            var section = new Section { BorderBrush = accent, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(0, 10, 0, 14) };
            foreach (var inner in quote) section.Blocks.Add(Block(inner, accent, navigate, depth + 1, imageSource)); return section;
        }
        if (b is CodeBlock code) return new Paragraph(new Run(code.Lines.ToString())) { FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(12), Background = new SolidColorBrush(Color.FromArgb(22, 100, 150, 130)), Margin = new Thickness(0, 10, 0, 14) };
        if (b is ThematicBreakBlock) return new Paragraph(new Run("────────────")) { Foreground = accent };
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12) };
        if (b is HeadingBlock h) { paragraph.FontSize = h.Level switch { 1 => 27, 2 => 22, 3 => 18, _ => 16 }; paragraph.FontWeight = FontWeights.SemiBold; paragraph.Margin = new Thickness(0, 12, 0, 14); }
        if (b is LeafBlock leaf)
        {
            if (leaf.Inline != null) paragraph.Inlines.AddRange(InlineGroup(leaf.Inline, accent, navigate, depth + 1, imageSource));
            else paragraph.Inlines.Add(new Run(leaf.Lines.ToString()));
        }
        return paragraph;
    }
    private static WpfInline Inline(Markdig.Syntax.Inlines.Inline inline, Brush accent, Action<string> navigate, int depth, Func<string, byte[]>? imageSource)
    {
        if (depth > 100) throw new InvalidDataException("Markdown 嵌套过深。");
        if (inline is LiteralInline literal) return new Run(literal.Content.ToString());
        if (inline is CodeInline code) return new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = new SolidColorBrush(Color.FromArgb(22, 100, 150, 130)) };
        if (inline is LineBreakInline) return new LineBreak();
        if (inline is HtmlInline html) return new Run(html.Tag);
        if (inline is AutolinkInline auto) return Link(auto.Url, [new Run(auto.Url)], accent, navigate);
        if (inline is ContainerInline container)
        {
            var children = InlineGroup(container, accent, navigate, depth + 1, imageSource);
            if (inline is LinkInline link)
            {
                if (link.IsImage)
                {
                    if (imageSource != null && NoteMedia.IsAsset(link.Url ?? ""))
                    {
                        try
                        {
                            var bitmap = ImageCodec.Decode(imageSource(link.Url!));
                            var picture = new System.Windows.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform, MaxHeight = 360, MaxWidth = 420, Margin = new Thickness(0, 10, 0, 10), ToolTip = "工作区本地图片" };
                            picture.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.FlowDocumentScrollViewer), 1), Converter = new PreviewImageWidth() });
                            return new InlineUIContainer(picture);
                        }
                        catch { return new Run("[图片不可用：文件缺失、损坏或尺寸超限]"); }
                    }
                    var imageText = new Span(new Run("[图片：")); imageText.Inlines.AddRange(children); imageText.Inlines.Add(new Run(" · 未加载，仅支持工作区本地附件]")); return imageText;
                }
                return Link(link.Url ?? "", children, accent, navigate);
            }
            Span span = inline is EmphasisInline e ? e.DelimiterCount >= 2 ? new Bold() : new Italic() : new Span();
            span.Inlines.AddRange(children); return span;
        }
        return new Run("");
    }
    private static WpfInline[] InlineGroup(ContainerInline container, Brush accent, Action<string> navigate, int depth, Func<string, byte[]>? imageSource)
    {
        var root = new Span(); var stack = new Stack<Span>(); stack.Push(root);
        foreach (var item in container)
        {
            if (item is HtmlInline html)
            {
                var match = System.Text.RegularExpressions.Regex.Match(html.Tag, "^<span style=\"color:(#[0-9a-fA-F]{6})\">$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var span = new Span { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(match.Groups[1].Value)) };
                    if (stack.Count > 100) throw new InvalidDataException("颜色嵌套过深。");
                    stack.Peek().Inlines.Add(span); stack.Push(span); continue;
                }
                if (html.Tag.Equals("</span>", StringComparison.OrdinalIgnoreCase) && stack.Count > 1) { stack.Pop(); continue; }
            }
            stack.Peek().Inlines.Add(Inline(item, accent, navigate, depth, imageSource));
        }
        var result = root.Inlines.Cast<WpfInline>().ToArray(); root.Inlines.Clear(); return result;
    }
    private sealed class PreviewImageWidth : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is double width ? Math.Max(80, Math.Min(420, width - 56)) : 240d;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private static WpfInline Link(string url, WpfInline[] children, Brush accent, Action<string> navigate, Func<string, byte[]>? imageSource = null)
    {
        var valid = MarkdownLinks.Target(url) != null || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
        if (!valid) { var span = new Span(); span.Inlines.AddRange(children); span.ToolTip = "该链接协议或路径不受支持"; return span; }
        var link = new Hyperlink { Foreground = accent, ToolTip = url }; link.Inlines.AddRange(children);
        link.Click += (_, _) => navigate(url); return link;
    }
}
