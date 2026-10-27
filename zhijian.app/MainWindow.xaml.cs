using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Zhijian.Core;
namespace zhijian.app;
public partial class MainWindow : Window
{
    private Workspace? workspace;
    private NoteRecord? current;
    private bool loading = true, trash, dirty, navigating, closing, busy, dark;
    private long editSequence, searchSequence, pageGeneration;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private CancellationTokenSource? jobCancellation;
    private readonly List<NoteRecord> visibleNotes = [];
    private string? smokeOutput;
    private string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zhijian", "last-workspace.txt");
    public MainWindow()
    {
        var testArgs = Environment.GetCommandLineArgs();
        if (testArgs.Length >= 3 && testArgs[1] == "--smoke")
        {
            Directory.CreateDirectory(testArgs[2]);
            var bindingLog = new System.Diagnostics.TextWriterTraceListener(Path.Combine(testArgs[2], "binding.log"));
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(bindingLog);
            Closed += (_, _) => { bindingLog.Flush(); System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingLog); bindingLog.Dispose(); };
        }
        InitializeComponent();
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); if (!busy) await SaveCurrent(); };
        searchTimer.Tick += async (_, _) => { searchTimer.Stop(); await RefreshList(); };
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); RenderPreview(); };
        loading = false;
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length >= 3 && args[1] == "--dialog-check")
        {
            try { CheckDialogs(args[2]); }
            catch (Exception ex) { Directory.CreateDirectory(args[2]); File.WriteAllText(Path.Combine(args[2], "dialog-error.txt"), ex.ToString()); }
            closing = true; Close(); return;
        }
        if (args.Length >= 3 && args[1] == "--close-check")
        {
            smokeOutput = args[2]; Directory.CreateDirectory(smokeOutput);
            Closed += (_, _) => File.WriteAllText(Path.Combine(smokeOutput, "close-check.txt"), "PASS 空工作区关闭及重复关闭请求");
            Close(); Close(); return;
        }
        if (args.Length >= 3 && args[1] == "--smoke")
        {
            smokeOutput = args[2];
            try { await SmokeTest(smokeOutput); }
            catch (Exception ex) { Directory.CreateDirectory(smokeOutput); File.WriteAllText(Path.Combine(smokeOutput, "ui-error.txt"), ex.ToString()); }
            finally
            {
                Closed += (_, _) => File.AppendAllText(Path.Combine(smokeOutput, "ui-checks.txt"), "PASS 已保存工作区正常关闭\n");
                Close();
            }
            return;
        }
        try
        {
            if (File.Exists(ConfigPath))
            {
                var path = File.ReadAllText(ConfigPath).Trim();
                if (Directory.Exists(path)) await SwitchWorkspace(path, false);
                else SaveStatus.Text = "上次工作区不可用，请重新打开或新建工作区。";
            }
        }
        catch (Exception ex) { Error("打开上次工作区", ex); }
    }
    private string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }
    private bool NeedWorkspace()
    {
        if (workspace != null) return true;
        Dialogs.Text(this, "开始使用知笺", "请先点击右上角“新建工作区”，选择空文件夹；已有笔记可选择“打开工作区”。"); return false;
    }
    private void Error(string operation, Exception ex)
    {
        SaveStatus.Text = $"{operation}失败：{ex.Message}";
        if (smokeOutput != null) throw new InvalidOperationException(operation, ex);
        Dialogs.Text(this, operation + "未完成", $"{ex.Message}\n\n当前编辑不会被自动丢弃。请检查路径、权限或磁盘空间后重试，也可另存正文。");
    }
    private async Task<bool> RunJob(string label, Func<CancellationToken, IProgress<string>, Task> action)
    {
        if (busy) return false;
        busy = true; saveTimer.Stop(); searchTimer.Stop(); BusyText.Text = label; BusyPanel.Visibility = Visibility.Visible;
        jobCancellation = new(); var progress = new Progress<string>(s => BusyText.Text = s);
        try { await action(jobCancellation.Token, progress); return true; }
        catch (OperationCanceledException) { SaveStatus.Text = "任务已取消。已完成的导入或导出保留，未完成备份标记为 .partial。"; return false; }
        catch (Exception ex) { Error(label, ex); return false; }
        finally { jobCancellation.Dispose(); jobCancellation = null; BusyPanel.Visibility = Visibility.Collapsed; busy = false; if (dirty) saveTimer.Start(); UpdateStatus(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { jobCancellation?.Cancel(); BusyText.Text = "正在安全停止，请稍候…"; }
    private async Task SwitchWorkspace(string path, bool create)
    {
        if (!await SaveCurrent(true)) return;
        if (workspace != null && string.Equals(Disk.Root(path), workspace.Path, StringComparison.OrdinalIgnoreCase)) return;
        Workspace? next = null;
        if (!await RunJob(create ? "正在创建工作区…" : "正在打开工作区…", async (_, p) => next = await Task.Run(() => new Workspace(path, create, p)))) return;
        workspace?.Dispose(); workspace = next!; current = null; dirty = false; trash = false;
        loading = true; SearchBox.Clear(); TagList.SelectedItems.Clear(); loading = false;
        WorkspaceName.Text = Path.GetFileName(workspace.Path); WorkspacePath.Text = workspace.Path; WorkspacePath.ToolTip = workspace.Path;
        ApplyTheme(workspace.Settings.Theme == "dark");
        if (smokeOutput == null)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!); Disk.Atomic(ConfigPath, Encoding.UTF8.GetBytes(workspace.Path)); }
            catch { SaveStatus.Text = "工作区已打开，但无法记住最近路径。"; }
        }
        await RefreshTags(); await RefreshList();
        if (visibleNotes.Count > 0) await OpenNote(visibleNotes[0].Id); else ClearEditor();
        if (workspace.Warnings.Count > 0) Dialogs.Text(this, "工作区检查结果", string.Join("\n\n", workspace.Warnings));
        UpdateStatus();
    }
    private async void CreateWorkspace_Click(object sender, RoutedEventArgs e) { if (busy) return; var path = PickFolder("新建工作区：请选择一个空文件夹"); if (path != null) await SwitchWorkspace(path, true); }
    private async void OpenWorkspace_Click(object sender, RoutedEventArgs e) { if (busy) return; var path = PickFolder("打开工作区：选择含 workspace.json 的文件夹"); if (path != null) await SwitchWorkspace(path, false); }
    private void UpdateStatus()
    {
        if (workspace == null) return;
        AllButton.Content = $"全部笔记    {workspace.Count}"; TrashButton.Content = $"回收站    {workspace.TrashCount}";
        IndexStatus.Text = workspace.IndexReady ? "本地工作区  ·  索引就绪" : "检索暂不可用  ·  请重建索引";
        ListHeading.Text = trash ? "回收站" : "全部笔记";
        SearchHint.Text = trash ? "回收站仅搜索标题" : "搜索标题与正文  ·  Ctrl+K";
        EmptyTrashButton.Visibility = trash ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task RefreshTags()
    {
        if (workspace == null) return;
        var selected = TagList.SelectedItems.Cast<string>().ToArray(); var tags = await Task.Run(workspace.AllTags);
        loading = true; TagList.ItemsSource = tags; foreach (var t in selected.Where(tags.Contains)) TagList.SelectedItems.Add(t); loading = false;
    }
    private async Task RefreshList(bool more = false)
    {
        if (workspace == null || busy) return;
        var seq = ++searchSequence; var ws = workspace; var query = SearchBox.Text; var tags = TagList.SelectedItems.Cast<string>().ToArray(); var recycle = trash;
        var offset = more ? visibleNotes.Count : 0; var generation = more ? (long?)pageGeneration : null;
        try
        {
            var page = await Task.Run(() => ws.Search(query, tags, recycle, offset, generation));
            if (seq != searchSequence || ws != workspace) return;
            if (!more) visibleNotes.Clear(); visibleNotes.AddRange(page.Items); pageGeneration = page.Generation;
            loading = true; NoteList.ItemsSource = visibleNotes.ToArray(); NoteList.SelectedItem = visibleNotes.FirstOrDefault(n => n.Id == current?.Id); loading = false;
            MoreButton.Visibility = page.HasMore ? Visibility.Visible : Visibility.Collapsed;
            ResultCount.Text = visibleNotes.Count == 0 ? "没有符合条件的笔记" : $"已显示 {visibleNotes.Count} 篇"; UpdateStatus();
        }
        catch (Exception ex)
        {
            if (seq != searchSequence) return;
            loading = true; NoteList.ItemsSource = null; loading = false; MoreButton.Visibility = Visibility.Collapsed;
            ResultCount.Text = "检索不可用"; SaveStatus.Text = ex.Message;
        }
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { if (loading) return; searchSequence++; searchTimer.Stop(); searchTimer.Start(); }
    private void Tags_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (loading) return; searchSequence++; searchTimer.Stop(); searchTimer.Start(); }
    private void ClearTags_Click(object sender, RoutedEventArgs e) => TagList.SelectedItems.Clear();
    private async void More_Click(object sender, RoutedEventArgs e) => await RefreshList(true);
    private async void All_Click(object sender, RoutedEventArgs e) => await SetView(false);
    private async void Trash_Click(object sender, RoutedEventArgs e) => await SetView(true);
    private async Task SetView(bool recycle)
    {
        if (busy || !NeedWorkspace() || !await SaveCurrent(true)) return;
        trash = recycle; ClearEditor(); loading = true; SearchBox.Clear(); TagList.SelectedItems.Clear(); loading = false;
        await RefreshList(); if (visibleNotes.Count > 0) await OpenNote(visibleNotes[0].Id);
    }
    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try { if (workspace != null) await Task.Run(() => workspace.SetTheme(dark ? "light" : "dark")); ApplyTheme(!dark); }
        catch (Exception ex) { Error("保存主题设置", ex); }
    }
    private void ApplyTheme(bool isDark)
    {
        dark = isDark;
        var colors = isDark ? new[] { "#161E1D", "#1E2927", "#192320", "#E3ECE8", "#A0B4AA", "#34453F", "#438970", "#30483D" } : new[] { "#F5F6F8", "#FFFFFF", "#F0F3F3", "#243330", "#6B7B77", "#E1E7E4", "#236B57", "#E7F1EC" };
        var names = new[] { "CanvasBrush", "PanelBrush", "SideBrush", "TextBrush", "MutedBrush", "LineBrush", "AccentBrush", "HoverBrush" };
        for (int i = 0; i < names.Length; i++) Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Resources["AccentTextBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#9AD9BB" : "#236B57"));
        if (isDark) Resources["AccentBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#32745B"));
        ThemeButton.Content = isDark ? "☀  浅色" : "◐  深色"; RenderPreview();
    }
}
