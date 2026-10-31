using System.IO;
using System.Windows;
using System.Windows.Input;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zhijian.Core;
namespace zhijian.app;

public partial class MainWindow
{
    private static string[] DroppedImages(IDataObject data) => data.GetDataPresent(DataFormats.FileDrop)
        ? (data.GetData(DataFormats.FileDrop) as string[] ?? []).Where(p => Path.GetExtension(p).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg").ToArray() : [];
    private void Body_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        e.Effects = !busy && current?.State == "ACTIVE" && DroppedImages(e.Data).Length > 0 ? DragDropEffects.Copy : DragDropEffects.None;
    }
    private async void Body_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Handled = true;
        if (busy || current?.State != "ACTIVE") return;
        var position = BodyBox.GetCharacterIndexFromPoint(e.GetPosition(BodyBox), true);
        if (position >= 0) BodyBox.Select(position, 0);
        await InsertDroppedImages(DroppedImages(e.Data));
    }
    private async Task InsertDroppedImages(string[] paths)
    {
        if (paths.Length > 20) { Dialogs.Text(this, "图片数量过多", "一次最多拖入20张PNG/JPEG图片。"); return; }
        var id = current?.Id; var ws = workspace;
        foreach (var path in paths)
        {
            if (current?.Id != id || workspace != ws || busy) break;
            await InsertImageFile(path); BodyBox.Select(BodyBox.SelectionStart + BodyBox.SelectionLength, 0);
        }
    }
    private async void Body_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
        if (!e.DataObject.GetDataPresent(DataFormats.Bitmap) && !e.DataObject.GetDataPresent(DataFormats.FileDrop)) return;
        e.CancelCommand();
        if (busy || current?.State != "ACTIVE") return;
        try
        {
            if (e.DataObject.GetData(DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource bitmap)
            { var copy = bitmap.Clone(); copy.Freeze(); await InsertImage(() => Task.Run(() => ImageCodec.Png(copy))); }
            else await InsertDroppedImages(DroppedImages(e.DataObject));
        }
        catch (Exception ex) { Error("粘贴图片", ex); }
    }
    private void OpenImage(string url, int start, int end)
    {
        if (busy || workspace == null || current == null) return;
        try
        {
            var original = BodyBox.Text; var id = current.Id;
            if (!MarkdownLinks.Parse(original).Descendants<LinkInline>().Any(l => l.IsImage && l.Url == url && l.Span.Start == start && l.Span.End == end))
            { RenderPreview(); return; }
            var width = Dialogs.ImageViewer(this, workspace.ReadImage(url), current.State == "ACTIVE");
            if (width.HasValue && current?.Id == id && BodyBox.Text == original) ResizeImage(start, end, width.Value);
        }
        catch (Exception ex) { Error("查看图片", ex); }
    }
    private void ResizeImage(int start, int end, int width)
    {
        if (busy || current?.State != "ACTIVE" || width is < 0 or > 1200) return;
        var image = MarkdownLinks.Parse(BodyBox.Text).Descendants<LinkInline>().FirstOrDefault(l => l.IsImage && l.Span.Start == start && l.Span.End == end);
        if (image == null || !NoteMedia.IsAsset(image.Url ?? "")) return;
        var source = BodyBox.Text.Substring(start, end - start + 1);
        var labelEnd = source.LastIndexOf("](", StringComparison.Ordinal);
        var label = labelEnd >= 0 ? source[..(labelEnd + 1)] : "![图片]";
        // Use the Markdown title field; other renderers still display a normal image.
        BodyBox.BeginChange();
        try { BodyBox.Select(start, end - start + 1); BodyBox.SelectedText = $"{label}({image.Url}" + (width == 0 ? ")" : $" \"width={width}\")"); }
        finally { BodyBox.EndChange(); }
        RenderPreview();
    }
}
