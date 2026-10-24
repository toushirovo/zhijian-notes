using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow
{
    // Explicit test mode uses its own workspace. Normal startup never generates sample notes.
    private async Task SmokeTest(string output)
    {
        Directory.CreateDirectory(output); CreateIcon(Path.Combine(output, "zhijian.ico")); var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add("PASS " + name); }
        await SwitchWorkspace(Path.Combine(output, "demo-workspace"), true);
        await NewNote(); TitleBox.Text = "从记录到连接：我的知识工作台"; TagsBox.Text = "入门, 知识管理";
        BodyBox.Text = "# 让想法在这里生长\n\n记录只是开始，**连接**让知识成为自己的。\n\n## 今天的学习计划\n\n- 阅读一章专业书籍\n- 整理课堂中的关键概念\n- 将新想法连接到已有笔记\n\n> 写下来，是思考的第一步。\n\n使用 `Ctrl+S` 保存，或等待自动保存。";
        await Task.Delay(2700);
        var first = current!;
        Check(!dirty && workspace!.Get(first.Id).Body.Contains("让想法"), "编辑与两秒自动保存");
        await NewNote(); TitleBox.Text = "学习方法 · 间隔复习"; TagsBox.Text = "学习方法";
        BodyBox.Text = $"# 间隔复习\n\n将复习安排在不同时间。\n\n[我的知识工作台](note:{first.Id})"; await SaveCurrent();
        Check(workspace!.Backlinks(first.Id).Length == 1, "双链索引");
        await NewNote(); TitleBox.Text = "阅读摘录 · 深度工作"; TagsBox.Text = "阅读, 学习方法"; BodyBox.Text = "# 深度工作\n\n为专注留出不被打断的时间。"; await SaveCurrent();
        await NewNote(); TitleBox.Text = "本周灵感收集"; TagsBox.Text = "灵感"; BodyBox.Text = "# 本周灵感\n\n- 一个值得继续探索的问题\n- 一段想要留存的表达"; await SaveCurrent();
        SearchBox.Text = "间隔"; await RefreshList(); Check(visibleNotes.Count == 1, "界面搜索");
        SearchBox.Clear(); await RefreshList(); await OpenNote(first.Id); Check(Backlinks.Items.Count == 1, "界面反向引用");
        await ChangeTrash(true); await SetView(true); Check(current?.Id == first.Id && BodyBox.IsReadOnly, "回收站只读");
        await ChangeTrash(false); await SetView(false); await OpenNote(first.Id);
        Check(current?.Id == first.Id && !BodyBox.IsReadOnly, "恢复笔记");
        UpdateLayout();
        foreach (var list in new[] { NoteList, TagList, Backlinks })
        {
            var item = list.ItemContainerGenerator.ContainerFromIndex(0) as System.Windows.Controls.ListBoxItem;
            Check(item != null && !System.Windows.Data.BindingOperations.IsDataBound(item, System.Windows.Controls.Control.HorizontalContentAlignmentProperty)
                && !System.Windows.Data.BindingOperations.IsDataBound(item, System.Windows.Controls.Control.VerticalContentAlignmentProperty),
                list.Name + " 对齐属性不依赖祖先绑定");
        }
        RenderPreview(); await Capture(Path.Combine(output, "light.png"));
        await Task.Run(() => workspace.SetTheme("dark")); ApplyTheme(true); await Capture(Path.Combine(output, "dark.png"));
        Check(workspace.Settings.Theme == "dark", "主题设置");
        Width = 1100; Height = 700; await Capture(Path.Combine(output, "compact.png"));
        Width = 1440; Height = 900; ApplyTheme(false);
        var unsafeDoc = SafePreview.Render("<script>alert(1)</script>\n\n![remote](https://example.com/a.png)\n\n[bad](javascript:alert(1))", Brushes.Black, Brushes.Green, _ => throw new Exception("不应自动导航"));
        Check(unsafeDoc.Blocks.Count > 0, "危险语法安全渲染");
        File.WriteAllLines(Path.Combine(output, "ui-checks.txt"), checks);
    }
    private static void CreateIcon(string path)
    {
        var visual = new DrawingVisual();
        using (var d = visual.RenderOpen())
        {
            d.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(35, 107, 87)), null, new Rect(0, 0, 64, 64), 15, 15);
            var pen = new Pen(Brushes.White, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            d.DrawGeometry(null, pen, Geometry.Parse("M 14,17 L 30,22 L 30,49 L 14,44 Z M 34,22 L 50,17 L 50,44 L 34,49 Z"));
            d.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(169, 220, 191)), 2), new Point(19, 27), new Point(25, 29));
            d.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(169, 220, 191)), 2), new Point(39, 29), new Point(45, 27));
        }
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var png = new MemoryStream(); encoder.Save(png);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
        writer.Write((byte)64); writer.Write((byte)64); writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write((uint)png.Length); writer.Write((uint)22); writer.Write(png.ToArray());
    }
    private async Task Capture(string path)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); UpdateLayout();
        var visual = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
