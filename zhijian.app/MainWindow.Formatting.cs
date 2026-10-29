using System.Windows;
using System.Windows.Controls;
namespace zhijian.app;
public partial class MainWindow
{
    private string editorLayout = "split";
    private void Layout_Click(object sender, RoutedEventArgs e) => SetEditorLayout((string)((Button)sender).Tag);
    private void SetEditorLayout(string mode)
    {
        editorLayout = mode;
        EditorSurface.Visibility = mode == "preview" ? Visibility.Collapsed : Visibility.Visible;
        PreviewSurface.Visibility = mode == "edit" ? Visibility.Collapsed : Visibility.Visible;
        EditorDivider.Visibility = mode == "split" ? Visibility.Visible : Visibility.Collapsed;
        EditorColumns.ColumnDefinitions[0].MinWidth = mode == "preview" ? 0 : 200;
        EditorColumns.ColumnDefinitions[2].MinWidth = mode == "edit" ? 0 : 200;
        EditorColumns.ColumnDefinitions[0].Width = mode == "preview" ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        EditorColumns.ColumnDefinitions[1].Width = new GridLength(mode == "split" ? 9 : 0);
        EditorColumns.ColumnDefinitions[2].Width = mode == "edit" ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        foreach (var button in LayoutButtons.Children.OfType<Button>())
            button.Style = (Style)FindResource((string)button.Tag == mode ? "PrimaryButton" : typeof(Button));
        if (mode != "edit") RenderPreview();
    }
    private void Format_Click(object sender, RoutedEventArgs e) => InsertMarkdown((string)((Button)sender).Tag);
    private void InsertMarkdown(string kind)
    {
        if (busy || current?.State != "ACTIVE") return;
        if (editorLayout == "preview") SetEditorLayout("split");
        int start = BodyBox.SelectionStart, length = BodyBox.SelectionLength;
        string selected = BodyBox.SelectedText, before = "", after = "", placeholder = "文字";
        switch (kind)
        {
            case "bold": before = after = "**"; break;
            case "italic": before = after = "*"; break;
            case "heading": before = "## "; placeholder = "标题"; break;
            case "list": before = "- "; placeholder = "列表项"; break;
            case "ordered": before = "1. "; placeholder = "列表项"; break;
            case "quote": before = "> "; placeholder = "引用内容"; break;
            case "code": before = "```\n"; after = "\n```"; placeholder = "代码"; break;
            case "link": before = "["; after = "](https://example.com)"; placeholder = "链接文字"; break;
            default: return;
        }
        bool block = kind is "heading" or "list" or "ordered" or "quote" or "code";
        string prefix = block && start > 0 && BodyBox.Text[start - 1] != '\n' ? "\n" : "";
        string content = length == 0 ? placeholder : selected;
        if (kind is "heading" or "list" or "ordered" or "quote")
            content = content.Replace("\r\n", "\n").Replace("\n", "\n" + before);
        string suffix = block && start + length < BodyBox.Text.Length && BodyBox.Text[start + length] != '\r' && BodyBox.Text[start + length] != '\n' ? "\n" : "";
        BodyBox.BeginChange();
        try { BodyBox.SelectedText = prefix + before + content + after + suffix; BodyBox.Select(start + prefix.Length + before.Length, content.Length); }
        finally { BodyBox.EndChange(); }
        BodyBox.Focus();
    }
}
