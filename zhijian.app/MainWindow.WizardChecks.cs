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
    private void CheckRestoreWizard(string output, List<string> checks)
    {
        string sourceWs = Path.Combine(output, "wizard-original"), backups = Path.Combine(output, "wizard-backups"), target = Path.Combine(output, "wizard-restored");
        Directory.CreateDirectory(sourceWs); Directory.CreateDirectory(backups); Directory.CreateDirectory(target);
        string backup, noteId;
        using (var ws = new Workspace(sourceWs, true)) { var n = ws.Create("向导恢复测试", "完整正文", ["标签"]); noteId = n.Id; ws.Create("第二篇", $"[引用](note:{n.Id})"); backup = ws.Backup(backups); }
        void Reject(string path) { try { Dialogs.ValidateRestoreTarget(backup, path, sourceWs); } catch (IOException) { return; } throw new Exception("不合法恢复位置未拒绝：" + path); }
        Reject(backup); Reject(backups); Reject(sourceWs); Directory.CreateDirectory(Path.Combine(backup, "nested")); Reject(Path.Combine(backup, "nested"));
        // Empty directories do not change a backup manifest; validation ignores directories.
        var occupied = Path.Combine(output, "occupied"); Directory.CreateDirectory(occupied); File.WriteAllText(Path.Combine(occupied, "keep.txt"), "保留"); Reject(occupied);
        checks.Add("PASS 恢复路径同目录/父子目录/当前工作区/非空目录保护");
        int stage = 0, ticks = 0; Exception? failure = null;
        IEnumerable<DependencyObject> Walk(DependencyObject root)
        { yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            var dialog = OwnedWindows.Cast<Window>().FirstOrDefault(w => w.IsVisible); if (dialog == null) return;
            try
            {
                if (++ticks > 100) throw new Exception("恢复向导检查超时");
                var elements = Walk(dialog).ToArray();
                Button? Find(string text) => elements.OfType<Button>().FirstOrDefault(b => Equals(b.Content, text));
                void Click(string text) => Find(text)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var input = elements.OfType<TextBox>().FirstOrDefault(t => t.Name == "WizardPath");
                if (stage == 0) { input!.Text = output; Click("检查备份"); stage++; }
                else if (stage == 1 && Find("检查备份")?.IsEnabled == true) { input!.Text = backup; Click("检查备份"); stage++; }
                else if (stage == 2 && Find("下一步")?.IsEnabled == true) { input!.Text = backup; Click("下一步"); stage++; }
                else if (stage == 3)
                {
                    if (Find("下一步") == null) throw new Exception("恢复到备份目录未被阻止");
                    input!.Text = target; Click("下一步"); stage++;
                }
                else if (stage == 4)
                {
                    dialog.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "restore-wizard.png"))) png.Save(file);
                    Click("上一步"); stage++;
                }
                else if (stage == 5) { if (input!.Text != target) throw new Exception("返回后目标路径丢失"); Click("下一步"); stage++; }
                else if (stage == 6) { timer.Stop(); Click("开始恢复"); }
            }
            catch (Exception ex) { failure = ex; timer.Stop(); dialog.Close(); }
        };
        timer.Start(); var plan = Dialogs.RestoreWizard(this, sourceWs); timer.Stop();
        if (failure != null) throw failure;
        if (plan == null || plan.Target != target || plan.Source != backup) throw new Exception("恢复计划不正确");
        Workspace.RestoreBackup(plan.Source, plan.Target);
        using (var restored = new Workspace(target))
            if (restored.Get(noteId).Body != "完整正文" || restored.Backlinks(noteId).Length != 1) throw new Exception("向导恢复数据不一致");
        checks.Add("PASS 恢复向导无效来源修正/无效目标修正/返回/确认及真实恢复");
    }
}
