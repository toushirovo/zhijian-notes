using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zhijian.Core;

namespace zhijian.app;
public partial class MainWindow
{
    private void ClearEditor()
    {
        current = null; dirty = false; saveTimer.Stop(); previewTimer.Stop();
        loading = true; TitleBox.Clear(); TagsBox.Clear(); BodyBox.Clear(); Backlinks.ItemsSource = null; loading = false;
        EditorPanel.Visibility = Visibility.Collapsed; EmptyPanel.Visibility = Visibility.Visible; WordCount.Text = "";
    }
    private async void Note_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || navigating || busy || NoteList.SelectedItem is not NoteRecord n || n.Id == current?.Id) return;
        await OpenNote(n.Id);
    }
    private async Task OpenNote(string id)
    {
        if (workspace == null || navigating) return;
        navigating = true;
        try
        {
            if (!await SaveCurrent(true)) { loading = true; NoteList.SelectedItem = visibleNotes.FirstOrDefault(n => n.Id == current?.Id); loading = false; return; }
            var content = await Task.Run(() => workspace.Get(id));
            loading = true; current = content.Record; TitleBox.Text = current.Title; TagsBox.Text = string.Join(", ", current.Tags!); BodyBox.Text = content.Body;
            bool readOnly = current.State != "ACTIVE"; TitleBox.IsReadOnly = TagsBox.IsReadOnly = BodyBox.IsReadOnly = readOnly;
            SaveButton.IsEnabled = LinkButton.IsEnabled = FormatToolbar.IsEnabled = MediaToolbar.IsEnabled = !readOnly; DeleteButton.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
            RestoreButton.Visibility = PurgeButton.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;
            NoteMeta.Text = $"{(readOnly ? "回收站 · 只读" : "笔记")}  /  {current.ShortId}   ·   更新于 {current.UpdatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
            NoteList.SelectedItem = visibleNotes.FirstOrDefault(n => n.Id == id); dirty = false; loading = false;
            EditorPanel.Visibility = Visibility.Visible; EmptyPanel.Visibility = Visibility.Collapsed;
            SaveStatus.Text = "✓ 已保存"; RenderPreview(); await RefreshBacklinks(); UpdateStatus();
        }
        catch (Exception ex) { loading = false; Error("打开笔记", ex); }
        finally { navigating = false; }
    }
    private void Editor_Changed(object sender, TextChangedEventArgs e)
    {
        if (loading || current == null || current.State != "ACTIVE") return;
        dirty = true; editSequence++; SaveStatus.Text = "● 未保存 · 停止编辑 2 秒后自动保存";
        saveTimer.Stop(); saveTimer.Start(); previewTimer.Stop(); previewTimer.Start();
    }
    private string[] EditorTags() => TagsBox.Text.Split([',', '，'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private async Task<bool> SaveCurrent(bool notify = false)
    {
        await saveGate.WaitAsync();
        try
        {
            if (!dirty || current == null || workspace == null) return true;
            saveTimer.Stop(); var note = current; var title = TitleBox.Text; var body = BodyBox.Text; var tags = EditorTags(); var seq = editSequence; var ws = workspace;
            SaveStatus.Text = "◌ 保存中…";
            try
            {
                var saved = await Task.Run(() => ws.Save(note.Id, note.Revision, title, body, tags));
                if (current?.Id == saved.Id && ws == workspace)
                {
                    current = saved; dirty = seq != editSequence;
                    SaveStatus.Text = dirty ? "● 未保存 · 有新的编辑" : $"✓ 已保存 {DateTime.Now:HH:mm:ss}";
                    if (dirty) saveTimer.Start(); else { await RefreshTags(); await RefreshList(); await RefreshBacklinks(); }
                }
                UpdateStatus(); return !dirty;
            }
            catch (Exception ex)
            {
                SaveStatus.Text = $"保存失败：{ex.Message} 当前编辑已保留，可重试或另存正文。";
                if (notify) Error("保存笔记", ex); return false;
            }
        }
        finally { saveGate.Release(); }
    }
    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveCurrent(true);
    private async void New_Click(object sender, RoutedEventArgs e) => await NewNote();
    private async Task NewNote()
    {
        if (busy || !NeedWorkspace() || !await SaveCurrent(true)) return;
        try
        {
            var n = await Task.Run(() => workspace!.Create("未命名笔记")); trash = false;
            loading = true; SearchBox.Clear(); TagList.SelectedItems.Clear(); loading = false;
            await RefreshList(); await OpenNote(n.Id); TitleBox.Focus(); TitleBox.SelectAll();
        }
        catch (Exception ex) { Error("新建笔记", ex); }
    }
    private void RenderPreview()
    {
        if (current == null) return;
        Preview.Document = SafePreview.Render(BodyBox.Text, (Brush)FindResource("TextBrush"), (Brush)FindResource("AccentTextBrush"), Navigate, workspace == null ? null : workspace.ReadImage);
        WordCount.Text = $"{Rules.Length(BodyBox.Text):N0} 字符";
    }
    private async Task RefreshBacklinks()
    {
        if (workspace == null || current == null) return;
        var id = current.Id; var links = await Task.Run(() => workspace.Backlinks(id));
        if (current?.Id != id) return;
        Backlinks.ItemsSource = links; BacklinkCount.Text = workspace.IndexReady ? links.Length == 0 ? "   暂无其他笔记引用此篇" : $"   {links.Length} 篇 · 双击打开" : "   索引不可用，请重建";
    }
    private async void Navigate(string url)
    {
        var id = MarkdownLinks.Target(url);
        if (id != null)
        {
            if (workspace?.All().Any(n => n.Id == id) != true) { Dialogs.Text(this, "暂时无法打开链接", "链接目标已删除或不在当前工作区。可从回收站恢复，或重新插入链接。"); return; }
            await OpenNote(id); return;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && Dialogs.Confirm(this, "打开外部网页？", "确认后将使用默认浏览器访问此地址。", $"{uri.Host}\n\n{url}", "打开网页"))
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Error("打开网页", ex); }
    }
    private void Link_Click(object sender, RoutedEventArgs e)
    {
        if (workspace == null || current?.State != "ACTIVE") return;
        var target = Dialogs.PickNote(this, workspace.All().Where(n => n.Id != current.Id).ToArray());
        if (target == null) return;
        BodyBox.SelectedText = $"[{MarkdownLinks.EscapeLabel(target.Title!)}](note:{target.Id})"; BodyBox.Focus();
    }
    private async void Backlink_Click(object sender, MouseButtonEventArgs e) { if (Backlinks.SelectedItem is NoteRecord n) await OpenNote(n.Id); }
    private async void Delete_Click(object sender, RoutedEventArgs e) => await ChangeTrash(true);
    private async void RestoreNote_Click(object sender, RoutedEventArgs e) => await ChangeTrash(false);
    private async Task ChangeTrash(bool remove)
    {
        if (current == null || workspace == null || !await SaveCurrent(true)) return;
        try
        {
            var n = current; await Task.Run(() => workspace.SetTrash(n.Id, n.Revision, remove)); ClearEditor(); await RefreshTags(); await RefreshList();
            if (visibleNotes.Count > 0) await OpenNote(visibleNotes[0].Id); SaveStatus.Text = remove ? "笔记已移入回收站，可随时恢复。" : "笔记已恢复，原双链继续有效。";
        }
        catch (Exception ex) { Error(remove ? "删除笔记" : "恢复笔记", ex); }
    }
}
