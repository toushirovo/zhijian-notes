using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow
{
    private string lastTextColor = "#3B82F6";
    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (busy || current?.State != "ACTIVE") return;
        var color = Dialogs.PickColor(this, lastTextColor); if (color == null) return;
        ApplyTextColor(color); if (color.Length > 0) { lastTextColor = color; ColorButton.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); } else ColorButton.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextBrush");
    }
    private void ApplyTextColor(string color)
    {
        if (busy || current?.State != "ACTIVE") return;
        if (color.Length > 0 && !Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$")) throw new ArgumentException("颜色格式不正确。");
        if (editorLayout == "preview") SetEditorLayout("split");
        int start = BodyBox.SelectionStart, length = BodyBox.SelectionLength; string content = BodyBox.SelectedText;
        foreach (Match match in Regex.Matches(BodyBox.Text, "<span style=\"color:#[0-9a-fA-F]{6}\">(?<text>.*?)</span>", RegexOptions.Singleline))
        {
            var inner = match.Groups["text"];
            if ((start >= inner.Index && start + length <= inner.Index + inner.Length) || (start == match.Index && length == match.Length))
            { start = match.Index; length = match.Length; content = inner.Value; break; }
        }
        if (length == 0 && color.Length == 0) return;
        if (length == 0) content = "彩色文字";
        // Each line is independently wrapped so Markdown block boundaries remain valid.
        string replacement = color.Length == 0 ? Regex.Replace(content, "</?span(?: style=\"color:#[0-9a-fA-F]{6}\")?>", "")
            : string.Join("\n", content.Replace("\r\n", "\n").Split('\n').Select(line => line.Length == 0 ? "" : $"<span style=\"color:{color.ToUpperInvariant()}\">{line}</span>"));
        BodyBox.BeginChange();
        try { BodyBox.Select(start, length); BodyBox.SelectedText = replacement; BodyBox.Select(start, replacement.Length); }
        finally { BodyBox.EndChange(); }
        BodyBox.Focus();
    }
    private async void Image_Click(object sender, RoutedEventArgs e)
    {
        if (busy || current?.State != "ACTIVE") return;
        var picker = new OpenFileDialog { Title = "插入本地图片（PNG/JPEG，最多10 MiB）", Filter = "图片|*.png;*.jpg;*.jpeg" };
        if (picker.ShowDialog(this) == true) await InsertImageFile(picker.FileName);
    }
    private async Task InsertImageFile(string path)
    {
        await InsertImage(async () => await Task.Run(() =>
        {
            if (new FileInfo(path).Length > NoteMedia.MaxImageBytes) throw new InvalidDataException("图片超过10 MiB，请压缩后再插入。");
            var bytes = File.ReadAllBytes(path); ImageCodec.Decode(bytes); return bytes;
        }));
    }
    private async void PasteImage_Click(object sender, RoutedEventArgs e)
    {
        if (busy || current?.State != "ACTIVE") return;
        try
        {
            var bitmap = Clipboard.GetImage();
            if (bitmap == null) { Dialogs.Text(this, "剪贴板中没有图片", "请先复制图片或使用Win+Shift+S截屏，再点击“粘贴图片”。"); return; }
            bitmap.Freeze(); await InsertImage(() => Task.Run(() => ImageCodec.Png(bitmap)));
        }
        catch (Exception ex) { Error("粘贴图片", ex); }
    }
    private async Task InsertImage(Func<Task<byte[]>> read)
    {
        if (busy || workspace == null || current?.State != "ACTIVE") return;
        var ws = workspace; var id = current.Id; int start = BodyBox.SelectionStart, length = BodyBox.SelectionLength; string? url = null;
        if (!await RunJob("正在将图片保存到工作区…", async (_, _) => { var bytes = await read(); url = await Task.Run(() => ws.AddImage(bytes)); })) return;
        if (workspace != ws || current?.Id != id) return;
        if (editorLayout == "preview") SetEditorLayout("split");
        BodyBox.Select(start, length); BodyBox.SelectedText = $"\n![图片]({url})\n";
        BodyBox.Focus(); RenderPreview(); await SaveCurrent(true);
    }
}
