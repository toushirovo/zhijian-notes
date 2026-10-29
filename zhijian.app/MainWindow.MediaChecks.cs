using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow
{
    private async Task CheckMedia(string output, Action<bool, string> check)
    {
        BodyBox.Text = "重要内容"; BodyBox.SelectAll(); ApplyTextColor("#E06C75");
        check(BodyBox.Text == "<span style=\"color:#E06C75\">重要内容</span>", "文字颜色写入Markdown");
        RenderPreview();
        var paragraph = (Paragraph)Preview.Document.Blocks.FirstBlock;
        check(paragraph.Inlines.OfType<Span>().Any(s => s.Foreground is SolidColorBrush brush && brush.Color == Color.FromRgb(224,108,117)), "预览显示指定文字颜色");
        BodyBox.Select(BodyBox.Text.IndexOf("重要", StringComparison.Ordinal), 0); ApplyTextColor("#3B82F6");
        check(BodyBox.Text.Contains("#3B82F6") && !BodyBox.Text.Contains("#E06C75"), "光标所在彩色文字可重新换色");
        ApplyTextColor(""); check(BodyBox.Text == "重要内容", "恢复默认文字颜色");
        BodyBox.Undo(); check(BodyBox.Text.Contains("#3B82F6"), "颜色操作支持撤销");
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRoundedRectangle(new LinearGradientBrush(Color.FromRgb(35,107,87), Color.FromRgb(73,119,179), 20), null, new Rect(0,0,720,240), 20,20);
            drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(35,255,255,255)), null, new Point(630,70), 140,140);
            drawing.DrawText(new FormattedText("知笺 · 图文笔记", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 40, Brushes.White, 1), new Point(38,55));
            drawing.DrawText(new FormattedText("让重要的文字与画面，一起留在本地。", System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 22, Brushes.White, 1), new Point(42,125));
        }
        var bitmap = new RenderTargetBitmap(720,240,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var bytes = ImageCodec.Png(bitmap); var file = Path.Combine(output, "fixture.png"); File.WriteAllBytes(file, bytes);
        BodyBox.Text = "# 图文，让记录更清晰\n\n<span style=\"color:#3B82F6\">蓝色标记知识点</span>，<span style=\"color:#E06C75\">红色提醒待办</span>。\n\n图片保存在工作区，不依赖原文件。\n";
        BodyBox.CaretIndex = BodyBox.Text.Length; await InsertImageFile(file);
        var url = NoteMedia.Assets(BodyBox.Text).Single();
        check(workspace!.ReadImage(url).SequenceEqual(bytes), "插入图片复制到工作区");
        File.Move(file, Path.Combine(output, "fixture-moved.png")); RenderPreview();
        var images = Preview.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineUIContainer>()).ToArray();
        check(images.Any(i => i.Child is Image img && img.Source != null), "原图移动后本地图片仍显示");
        await SaveCurrent(); check(workspace.Get(current!.Id).Body.Contains(url), "图片引用持久保存");
        var remoteReads = 0; SafePreview.Render("![远程](https://example.com/a.png)", Brushes.Black, Brushes.Green, _ => { }, _ => { remoteReads++; return bytes; });
        check(remoteReads == 0, "远程图片不会触发读取");
        var invalid = SafePreview.Render("<span style=\"color:red\" onclick=\"alert(1)\">文字</span>", Brushes.Black, Brushes.Green, _ => throw new Exception("不能执行脚本"));
        check(new TextRange(invalid.ContentStart, invalid.ContentEnd).Text.Contains("onclick"), "仅允许指定颜色语法其余HTML按文字处理");
    }
}
