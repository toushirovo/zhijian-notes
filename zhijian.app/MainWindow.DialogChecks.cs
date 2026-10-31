using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow
{
    private void CheckDialogs(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Inspect(string name, Action show, string button, Action<Window>? prepare = null)
        {
            Exception? failure = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                var dialog = OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible);
                if (dialog == null) return;
                timer.Stop();
                try
                {
                    prepare?.Invoke(dialog);
                    dialog.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(dialog); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(stream);
                    IEnumerable<DependencyObject> Walk(DependencyObject root)
                    {
                        yield return root;
                        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
                    }
                    var target = Walk(dialog).OfType<Button>().First(b => Equals(b.Content, button));
                    target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    checks.Add("PASS " + name);
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            };
            timer.Start(); try { show(); } finally { timer.Stop(); }
            if (failure != null) throw failure;
        }
        foreach (var theme in new[] { false, true })
        {
            ApplyTheme(theme); var suffix = theme ? "dark" : "light";
            Inspect("help-" + suffix, () => Dialogs.Help(this, HelpText), "开始记录");
            var report = new BatchReport("导入", [new("课堂笔记.md", "SUCCESS"), new("阅读摘录.md", "SUCCESS", Message: "未匹配的相对链接已转为普通文本"), new("超限正文.md", "FAILED", Message: "超过 1 MiB。"), new("待处理.md", "SKIPPED", Message: "已取消")]);
            Inspect("report-" + suffix, () => Dialogs.Report(this, report), "完成");
            bool result = true;
            Inspect("delete-" + suffix, () => result = Dialogs.Confirm(this, "永久删除 2 篇笔记？", "删除后，这些笔记将无法从回收站恢复。", "已有备份仍可能包含这些内容。\n\n• 课堂笔记\n• 阅读摘录", "永久删除", true), "取消");
            if (result) throw new Exception("取消删除返回了确认结果");
        }
        bool accepted = false;
        Inspect("confirm-action", () => accepted = Dialogs.Confirm(this, "测试确认", "此测试不会执行真实删除。", "只检查确认返回值。", "永久删除", true), "永久删除");
        if (!accepted) throw new Exception("确认按钮没有返回确认结果");
        string closeChoice = "";
        Inspect("close-cancel", () => closeChoice = Dialogs.ClosingChoice(this), "继续编辑");
        if (closeChoice != "cancel") throw new Exception("继续编辑未取消关闭");
        bool importAccepted = false;
        Inspect("import-preview", () => importAccepted = Dialogs.PreviewImport(this, [new("导入甲.md", true, 0, ["缺少同批目标：导入乙.md。请返回并同时选择目标文件。"], null)]), "返回选文件");
        if (importAccepted) throw new Exception("返回选文件未取消导入");
        string? chosenColor = null;
        Inspect("color-picker", () => chosenColor = Dialogs.PickColor(this, "#3B82F6"), "应用颜色", dialog =>
        {
            IEnumerable<DependencyObject> Walk(DependencyObject root)
            { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
            var elements = Walk(dialog).ToArray(); var hex = elements.OfType<TextBox>().Single(x => x.Name == "ColorHex");
            var apply = elements.OfType<Button>().Single(x => Equals(x.Content, "应用颜色"));
            hex.Text = "#BAD"; if (apply.IsEnabled) throw new Exception("无效颜色未禁用应用按钮");
            hex.Text = "#E06C75"; if (!apply.IsEnabled) throw new Exception("有效颜色未启用应用按钮");
        });
        if (chosenColor != "#E06C75") throw new Exception("颜色选择返回错误");
        CheckRestoreWizard(output, checks);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.SeaGreen, null, new Rect(0, 0, 640, 300));
            drawing.DrawEllipse(Brushes.LightBlue, null, new Point(430, 150), 110, 110);
        }
        var imageBitmap = new RenderTargetBitmap(640, 300, 96, 96, PixelFormats.Pbgra32); imageBitmap.Render(visual);
        var imageBytes = ImageCodec.Png(imageBitmap);
        foreach (var theme in new[] { false, true })
        {
            ApplyTheme(theme); int? size = null;
            Inspect("image-viewer-" + (theme ? "dark" : "light"), () => size = Dialogs.ImageViewer(this, imageBytes, true), "应用显示尺寸", dialog =>
            {
                IEnumerable<DependencyObject> Walk(DependencyObject root)
                { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
                var elements = Walk(dialog).ToArray();
                elements.OfType<ComboBox>().Single().SelectedIndex = 3;
                elements.OfType<Slider>().Single().Value = 150;
                if (elements.OfType<Image>().Single().Width != 975) throw new Exception("图片查看缩放未生效");
            });
            if (size != 720) throw new Exception("图片尺寸选择返回错误");
        }
        int? readOnlySize = 1;
        Inspect("image-viewer-readonly", () => readOnlySize = Dialogs.ImageViewer(this, imageBytes, false), "关闭");
        if (readOnlySize != null) throw new Exception("只读图片不应修改尺寸");
        File.WriteAllLines(Path.Combine(output, "dialog-checks.txt"), checks);
    }
}
