using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Zhijian.Core;

public static class MarkdownLinks
{
    // Precise spans let migration rewrite link nodes without touching code or literal text.
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);
    public static IEnumerable<LinkInline> Links(string text) => Parse(text).Descendants<LinkInline>().Where(l => !l.IsImage);
    public static string? Target(string? url) => url?.StartsWith("note:", StringComparison.Ordinal) == true && Rules.IsId(url[5..]) ? url[5..] : null;
    public static IEnumerable<string> Targets(string text) => Links(text).Select(l => Target(l.Url)).OfType<string>().Distinct();
    public static string EscapeLabel(string label) => label.Replace("\\", "\\\\").Replace("[", "\\[").Replace("]", "\\]");
    public static string Rewrite(string body, Func<string, string?> map, bool images = false)
    {
        var changes = new List<(int Start, int End, string Text)>();
        foreach (var link in Parse(body).Descendants<LinkInline>().Where(l => l.IsImage == images))
        {
            var replacement = map(link.Url ?? ""); if (replacement == null) continue;
            if (replacement.Length > 0 && link.UrlSpan.Start > link.Span.Start && link.UrlSpan.End <= link.Span.End && link.UrlSpan.Length > 0)
                changes.Add((link.UrlSpan.Start, link.UrlSpan.End, replacement));
            else
            {
                var label = link.FirstChild != null && link.LastChild != null ? body[link.FirstChild.Span.Start..(link.LastChild.Span.End + 1)] : EscapeLabel(link.Url ?? "链接");
                changes.Add((link.Span.Start, link.Span.End, replacement == "" ? label : $"{(images ? "!" : "")}[{label}]({replacement})"));
            }
        }
        foreach (var change in changes.OrderByDescending(c => c.Start)) body = body[..change.Start] + change.Text + body[(change.End + 1)..];
        return body;
    }
}
