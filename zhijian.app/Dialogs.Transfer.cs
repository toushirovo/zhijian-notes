using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Zhijian.Core;
namespace zhijian.app;
internal static partial class Dialogs
{
    public static bool PreviewImport(Window owner, ImportPreviewItem[] items)
    {
        var w = Shell(owner, "导入前检查", 820, 700);
        var body = Frame(w, "知笺  /  导入 · 第2步", "确认文件与笔记连接", $"已选择 {items.Length} 个文件 · 可导入 {items.Count(i => i.CanImport)} 个 · 可连接 {items.Sum(i => i.MappedLinks)} 处", out var actions);
        Card(w, body, "导入方式", "生成独立的新笔记，不覆盖已有笔记。相互链接的文件需要同批选中；缺失的相对链接会变为普通文字。预检之后文件如有变化，执行时会再次检查。");
        foreach (var item in items)
            Card(w, body, (item.CanImport ? item.Warnings.Length > 0 ? "需要留意 · " : "可导入 · " : "无法导入 · ") + Path.GetFileName(item.Path), item.Path + "\n" + (item.Error ?? $"可映射链接：{item.MappedLinks}" + (item.Warnings.Length == 0 ? "" : "\n" + string.Join("\n", item.Warnings))));
        bool proceed = false;
        Action(w, actions, "返回选文件", () => w.Close(), false, true);
        var next = Action(w, actions, "确认导入", () => { proceed = true; w.Close(); }, true);
        next.IsEnabled = items.Any(i => i.CanImport);
        w.ShowDialog(); return proceed;
    }
    internal static void ValidateRestoreTarget(string source, string target, string? workspace)
    {
        source = Disk.Root(source); target = Disk.Root(target);
        if (Disk.Inside(target, source) || Disk.Inside(source, target)) throw new IOException("恢复位置必须在备份文件夹之外，也不能是包含备份的上级文件夹。建议在D盘另建一个空文件夹。");
        if (workspace != null && (Disk.Inside(target, workspace) || Disk.Inside(workspace, target))) throw new IOException("恢复位置必须独立于当前工作区，请另选空文件夹。");
        if (!Directory.Exists(target) || Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("请选择已经创建的空文件夹；里面不能有其他文件。");
    }
    public sealed record RestorePlan(string Source, string Target);
    public static RestorePlan? RestoreWizard(Window owner, string? currentWorkspace)
    {
        var w = Shell(owner, "从备份恢复", 780, 650);
        var body = Frame(w, "知笺  /  数据恢复", "一步一步，找回你的笔记", "选择备份 → 检查备份 → 选择空目录 → 确认恢复", out var actions);
        int step = 0; string source = "", target = ""; BackupManifest? manifest = null;
        RestorePlan? plan = null; bool processing = false;
        using var cancellation = new CancellationTokenSource();
        var message = Label("", 13);
        var pathBox = new TextBox { Name = "WizardPath", Margin = new Thickness(0, 8, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(pathBox, "文件夹路径");
        Button? next = null, back = null, browse = null;
        void Draw()
        {
            body.Children.Clear(); message.Text = "";
            Card(w, body, step == 0 ? "01  选择完整备份" : step == 1 ? "02  选择恢复位置" : "03  确认恢复", step == 0 ? "选择直接包含 manifest.json 的 backup-… 文件夹。不要选择里面的 bodies 或 records。" : step == 1 ? "请选择独立的空文件夹，例如 D:\\知笺恢复结果。备份来源和恢复位置不能互相包含。" : "即将恢复到下面的目录，原工作区和备份保持不变。");
            if (manifest != null) Card(w, body, "备份检查通过", $"备份时间：{manifest.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}\n有效笔记：{manifest.NoteCount} 篇    回收站：{manifest.TrashCount} 篇\n备份来源：{source}");
            if (step < 2)
            {
                pathBox.Text = step == 0 ? source : target; body.Children.Add(Label(step == 0 ? "备份来源" : "恢复到空文件夹", 13)); body.Children.Add(pathBox);
                browse = new Button { Content = "浏览文件夹…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
                browse.Click += (_, _) => { var picker = new OpenFolderDialog { Title = step == 0 ? "选择含manifest.json的备份文件夹" : "选择工作区之外的空文件夹" }; if (picker.ShowDialog(w) == true) pathBox.Text = picker.FolderName; };
                body.Children.Add(browse);
            }
            else Card(w, body, "恢复位置", target);
            body.Children.Add(message);
            next!.Content = step == 0 ? "检查备份" : step == 1 ? "下一步" : "开始恢复"; back!.IsEnabled = step > 0;
        }
        Action(w, actions, "取消", () => { if (processing) { cancellation.Cancel(); message.Text = "正在停止校验…"; } else w.Close(); }, false, true);
        back = Action(w, actions, "上一步", () => { step--; if (step == 0) manifest = null; Draw(); });
        next = Action(w, actions, "检查备份", async () =>
        {
            try
            {
                if (step == 0)
                {
                    source = pathBox.Text.Trim(); if (source.Length == 0) throw new IOException("请先选择备份来源。");
                    processing = true; next!.IsEnabled = back!.IsEnabled = pathBox.IsEnabled = browse!.IsEnabled = false; message.Text = "正在校验文件完整性，请稍候…";
                    manifest = await Task.Run(() => Workspace.ValidateBackup(source, cancellation.Token));
                    source = Disk.Root(source); step = 1;
                }
                else if (step == 1) { target = pathBox.Text.Trim(); if (target.Length == 0) throw new IOException("请先选择恢复位置。"); ValidateRestoreTarget(source, target, currentWorkspace); target = Disk.Root(target); step = 2; }
                else { ValidateRestoreTarget(source, target, currentWorkspace); plan = new(source, target); w.Close(); return; }
                Draw();
            }
            catch (OperationCanceledException) { processing = false; w.Close(); }
            catch (Exception ex) { message.Text = ex.Message; }
            finally { processing = false; next!.IsEnabled = pathBox.IsEnabled = true; back!.IsEnabled = step > 0; if (browse != null) browse.IsEnabled = true; }
        }, true);
        w.Closing += (_, e) => { if (processing) { e.Cancel = true; cancellation.Cancel(); message.Text = "正在停止校验…"; } };
        Draw(); w.ShowDialog(); return plan;
    }
}
