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
        void Inspect(string name, Action show, string button)
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
        File.WriteAllLines(Path.Combine(output, "dialog-checks.txt"), checks);
    }
}
