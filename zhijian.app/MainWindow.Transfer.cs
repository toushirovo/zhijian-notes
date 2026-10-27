using System.IO;
using System.Windows;
using Microsoft.Win32;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow
{
    private async void Purge_Click(object sender, RoutedEventArgs e)
    { if (workspace != null && current?.State == "TRASH") await PurgeNotes([current]); }
    private async void EmptyTrash_Click(object sender, RoutedEventArgs e) { if (workspace != null) await PurgeNotes(workspace.All(true)); }
    private async Task PurgeNotes(NoteRecord[] notes)
    {
        if (notes.Length == 0) return;
        if (!Dialogs.Confirm(this, $"永久删除 {notes.Length} 篇笔记？", "删除后，这些笔记将无法从回收站恢复。", "已有备份仍可能包含这些内容。\n\n" + string.Join("\n", notes.Take(8).Select(n => "• " + n.Title)) + (notes.Length > 8 ? $"\n以及其他 {notes.Length - 8} 篇笔记" : ""), "永久删除", true)) return;
        var report = new BatchReport("永久删除", []);
        await RunJob("正在清理回收站…", async (ct, p) => await Task.Run(() =>
        {
            foreach (var n in notes)
            {
                if (ct.IsCancellationRequested) { report.Items.Add(new(n.Title!, "SKIPPED", n.Id, "已取消")); continue; }
                try { workspace!.Purge(n.Id, n.Revision); report.Items.Add(new(n.Title!, "SUCCESS", n.Id)); }
                catch (Exception ex) { report.Items.Add(new(n.Title!, "FAILED", n.Id, ex.Message)); }
                p.Report($"已处理 {report.Items.Count}/{notes.Length}");
            }
        }));
        ClearEditor(); await RefreshList(); Dialogs.Report(this, report);
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedWorkspace() || !await SaveCurrent(true)) return;
        var d = new OpenFileDialog { Filter = "Markdown 文件|*.md", Multiselect = true, Title = "选择要导入的 Markdown 文件" }; if (d.ShowDialog(this) != true) return;
        BatchReport? report = null;
        if (await RunJob("正在导入 Markdown…", async (ct, p) => report = await Task.Run(() => workspace!.Import(d.FileNames, p, ct))))
        {
            trash = false; await RefreshTags(); await RefreshList();
            var id = report!.Items.FirstOrDefault(i => i.Status == "SUCCESS")?.NoteId; if (id != null) await OpenNote(id);
            Dialogs.Report(this, report);
        }
    }
    private async void ExportAll_Click(object sender, RoutedEventArgs e) => await ExportNotes(false);
    private async void ExportOne_Click(object sender, RoutedEventArgs e) => await ExportNotes(true);
    private async Task ExportNotes(bool one)
    {
        if (!NeedWorkspace() || (one && current == null) || !await SaveCurrent(true)) return;
        var path = PickFolder("选择导出位置（工作区之外）"); if (path == null) return; var ids = one ? new[] { current!.Id } : null;
        BatchReport? report = null;
        if (await RunJob("正在导出笔记…", async (ct, p) => report = await Task.Run(() => workspace!.Export(path, ids, p, ct)))) Dialogs.Report(this, report!);
    }
    private bool SaveAs()
    {
        if (current == null) return false;
        var dialog = new SaveFileDialog { Filter = "Markdown 文件|*.md", FileName = current.Id + ".md", Title = "另存当前编辑内容" };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            if (workspace != null && Disk.Inside(Path.GetFullPath(dialog.FileName), workspace.Path)) throw new IOException("请选择工作区外的目录，避免改动应用管理的文件。");
            Disk.Atomic(dialog.FileName, Rules.Utf8.GetBytes(BodyBox.Text)); SaveStatus.Text = "当前正文已另存；工作区中的保存状态不变。"; return true;
        }
        catch (Exception ex) { Error("另存正文", ex); return false; }
    }
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();
    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedWorkspace() || !await SaveCurrent(true)) return;
        var path = PickFolder("选择备份位置（工作区之外，建议另一磁盘）"); if (path == null) return;
        string? output = null;
        if (await RunJob("正在创建完整备份…", async (ct, p) => output = await Task.Run(() => workspace!.Backup(path, p, ct)))) Dialogs.Text(this, "备份完成", "备份包含笔记、标签、双链、回收站、主题设置和校验清单。\n\n" + output + "\n\n请妥善保管整个备份文件夹。后续删除笔记不会删除旧备份中的内容。");
    }
    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!await SaveCurrent(true)) return;
        var source = PickFolder("选择备份包（含 manifest.json 的文件夹）"); if (source == null) return;
        var target = PickFolder("选择新的空文件夹作为恢复目标"); if (target == null) return;
        if (workspace != null && Disk.Inside(Disk.Root(target), workspace.Path)) { Dialogs.Text(this, "请选择独立的恢复目录", "恢复目录不能放在当前工作区内。请在工作区之外新建一个空文件夹。"); return; }
        if (await RunJob("正在校验并恢复备份…", async (ct, p) => await Task.Run(() => Workspace.RestoreBackup(source, target, p, ct))))
        { await SwitchWorkspace(target, false); SaveStatus.Text = "已恢复到新的工作区，原工作区未改动。"; }
    }
    private async void Rebuild_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedWorkspace() || !await SaveCurrent(true)) return;
        await RunJob("正在重建索引…", async (ct, p) => await Task.Run(() => workspace!.RebuildIndex(p, ct))); await RefreshList(); await RefreshBacklinks();
    }
}
