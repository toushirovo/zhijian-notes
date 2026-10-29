using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Zhijian.Core;

if (args.Length > 0 && args[0] == "--perf") { Performance.Run(args[1]); return; }
if (args.Length > 0 && args[0] == "--crash")
{
    using var crashing = new Workspace(args[1], faultInjector: stage => { if (stage == args[2]) Environment.Exit(73); });
    var note = crashing.All()[0]; crashing.Save(note.Id, note.Revision, note.Title!, "新提交的内容", note.Tags!);
    return;
}
var root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "ZhijianChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root); var results = new List<string>(); int failed = 0;
void Test(string name, Action action)
{
    try { action(); results.Add("PASS " + name); Console.WriteLine(results[^1]); }
    catch (Exception ex) { failed++; results.Add("FAIL " + name + "\n" + ex); Console.WriteLine(results[^1]); }
}
void Check(bool ok, string message = "断言失败") { if (!ok) throw new Exception(message); }
void Throws(Action action) { try { action(); } catch { return; } throw new Exception("预期拒绝，但操作成功了"); }
string Folder(string name) { var p = Path.Combine(root, name); Directory.CreateDirectory(p); return p; }
using (var w = new Workspace(Folder("workspace"), true))
{
    var a = w.Create("数据库事务", "# ACID\nSQLite 事务 100% _literal_ 'quoted'", ["课程", "SQL", "sql"]);
    var b = w.Create("资料", "sqlite 事务", ["项目"]);
    Test("FR01 单工作区独占锁", () => Throws(() => { using var other = new Workspace(w.Path); }));
    Test("FR02 同名不同 ID", () => { var n = w.Create("数据库事务"); Check(n.Id != a.Id); w.SetTrash(n.Id, n.Revision, true); });
    Test("FR02 Unicode 标题和空标题限制", () => { Throws(() => w.Create("  ")); Throws(() => w.Create(new string('字', 101))); Check(w.Create(string.Concat(Enumerable.Repeat("😀", 100))).Title!.EnumerateRunes().Count() == 100); });
    Test("FR04 标签大小写去重", () => Check(a.Tags!.Length == 2));
    Test("FR04 标签数量与长度", () => { Throws(() => w.Create("坏标签", tags: Enumerable.Range(0, 21).Select(x => x.ToString()))); Throws(() => w.Create("坏标签", tags: [new string('字', 21)])); });
    Test("FR05 中文和英文子串检索", () => { Check(w.Search("SQLITE", []).Items.Count == 2); Check(w.Search("事务", ["课程"]).Items.Single().Id == a.Id); Check(w.Search("事务", ["课程", "项目"]).Items.Count == 0); });
    Test("FR05 SQL 特殊字符按字面处理", () => { Check(w.Search("%", []).Items.Single().Id == a.Id); Check(w.Search("'quoted'", []).Items.Single().Id == a.Id); Check(w.Search("' OR 1=1--", []).Items.Count == 0); });
    Test("FR03 保存与版本冲突", () => { var old = a; a = w.Save(a.Id, a.Revision, a.Title!, "新正文 SQLite 事务 100%", a.Tags!); Check(w.Get(a.Id).Body.StartsWith("新正文")); Throws(() => w.Save(a.Id, old.Revision, "冲突", "坏", [])); });
    Test("FR03 正文超限保留旧版", () => { var old = w.Get(a.Id).Body; Throws(() => w.Save(a.Id, a.Revision, a.Title!, new string('x', Rules.MaxBodyBytes + 1), [])); Check(w.Get(a.Id).Body == old); });
    var linkText = $"[目标](note:{b.Id})\n[第二次](note:{b.Id})\n\n```\n[代码](note:{a.Id})\n```\n\n`[行内](note:{a.Id})`";
    a = w.Save(a.Id, a.Revision, a.Title!, linkText, a.Tags!);
    Test("FR06 反向引用去重且忽略代码", () => { Check(w.Backlinks(b.Id).Single().Id == a.Id); Check(!MarkdownLinks.Targets(linkText).Contains(a.Id)); });
    Test("FR06 重命名不破坏引用", () => { b = w.Save(b.Id, b.Revision, "资料新名称", w.Get(b.Id).Body, b.Tags!); Check(w.Backlinks(b.Id).Single().Id == a.Id); });
    Test("FR07 删除和恢复保留稳定 ID", () => { b = w.SetTrash(b.Id, b.Revision, true); Check(w.Search("sqlite", []).Items.Count == 0); Check(w.Backlinks(b.Id).Length == 1); b = w.SetTrash(b.Id, b.Revision, false); Check(w.Search("sqlite", []).Items.Single().Id == b.Id); });
    Test("FR07 删除来源清除反向引用", () => { a = w.SetTrash(a.Id, a.Revision, true); Check(w.Backlinks(b.Id).Length == 0); a = w.SetTrash(a.Id, a.Revision, false); Check(w.Backlinks(b.Id).Length == 1); });
    Test("FR08 导出链接转换不改代码", () =>
    {
        var report = w.Export(Folder("exports"), [a.Id, b.Id]); Check(report.FailedCount == 0);
        var text = File.ReadAllText(Path.Combine(report.OutputPath!, a.Id + ".md"));
        Check(text.Contains($"./{b.Id}.md")); Check(text.Contains($"[代码](note:{a.Id})"));
        using var imported = new Workspace(Folder("reimport"), true);
        var importedReport = imported.Import(Directory.GetFiles(report.OutputPath!, "*.md")); Check(importedReport.SuccessCount == 2);
        var source = imported.All().Single(n => n.Title == a.Id); var target = imported.All().Single(n => n.Title == b.Id);
        Check(source.Id != a.Id && target.Id != b.Id); Check(imported.Backlinks(target.Id).Single().Id == source.Id);
    });
    Test("FR08 部分失败与编码校验", () =>
    {
        var input = Folder("import-input"); File.WriteAllBytes(Path.Combine(input, "bad.md"), [0xff, 0xfe, 0x80]); File.WriteAllText(Path.Combine(input, "ok.md"), "有效正文", new UTF8Encoding(false));
        var report = w.Import(Directory.GetFiles(input)); Check(report.SuccessCount == 1 && report.FailedCount == 1);
    });
    Test("FR08 批内 note UUID 重映射", () =>
    {
        var input = Folder("id-import"); var x = Guid.NewGuid().ToString(); var y = Guid.NewGuid().ToString();
        File.WriteAllText(Path.Combine(input, x + ".md"), $"[目标](note:{y})"); File.WriteAllText(Path.Combine(input, y + ".md"), "目标正文");
        var report = w.Import(Directory.GetFiles(input)); var sx = report.Items.Single(i => Path.GetFileNameWithoutExtension(i.Source) == x); var sy = report.Items.Single(i => Path.GetFileNameWithoutExtension(i.Source) == y);
        Check(w.Backlinks(sy.NoteId!).Single().Id == sx.NoteId);
    });
    Test("FR08 导入取消", () => { var ct = new CancellationToken(true); var report = w.Import(["unused.md"], cancel: ct); Check(report.SuccessCount == 0 && report.Items[0].Status == "SKIPPED"); });
    Test("FR09 禁止向工作区内部备份或导出", () => { Throws(() => w.Backup(w.Path)); Throws(() => w.Export(w.Path)); });
    string backup = "";
    Test("FR09 完整备份与逐字节恢复", () =>
    {
        w.SetTheme("dark"); backup = w.Backup(Folder("backups")); var manifest = Workspace.ValidateBackup(backup); Check(manifest.TrashCount == w.TrashCount);
        var target = Folder("restored"); Workspace.RestoreBackup(backup, target);
        foreach (var f in manifest.Files) Check(File.ReadAllBytes(Path.Combine(backup, f.Path)).SequenceEqual(File.ReadAllBytes(Path.Combine(target, f.Path))), f.Path);
        using var restored = new Workspace(target); Check(restored.Count == w.Count && restored.TrashCount == w.TrashCount && restored.Settings.Theme == "dark"); Check(restored.Backlinks(b.Id).Single().Id == a.Id);
    });
    Test("FR09 恢复拒绝非空目标", () => { var target = Folder("nonempty"); File.WriteAllText(Path.Combine(target, "user.txt"), "保留"); Throws(() => Workspace.RestoreBackup(backup, target)); Check(File.ReadAllText(Path.Combine(target, "user.txt")) == "保留"); });
    Test("FR09 校验拒绝额外文件", () => { var extra = Path.Combine(backup, "extra.txt"); File.WriteAllText(extra, "extra"); Throws(() => Workspace.ValidateBackup(backup)); File.Delete(extra); });
    Test("FR09 校验拒绝损坏正文且不触碰目标", () =>
    {
        var file = Directory.GetFiles(Path.Combine(backup, "bodies"), "*.md", SearchOption.AllDirectories)[0]; var bytes = File.ReadAllBytes(file); File.WriteAllText(file, "被损坏");
        var target = Folder("corrupt-restore"); Throws(() => Workspace.RestoreBackup(backup, target)); Check(!Directory.EnumerateFileSystemEntries(target).Any()); File.WriteAllBytes(file, bytes);
    });
    Test("FR09 路径穿越拒绝", () => { Throws(() => Disk.SafeFile(backup, "../outside")); Throws(() => Disk.SafeFile(backup, "D:/evil")); Throws(() => Disk.SafeFile(backup, "records/../../evil")); });
    Test("FR11 索引重建结果一致", () => { w.RebuildIndex(); Check(w.Backlinks(b.Id).Single().Id == a.Id); Check(w.Search("sqlite", []).Items.Single().Id == b.Id); });
    Test("FR07 永久删除墓碑", () => { var n = w.Create("待清除", "正文"); n = w.SetTrash(n.Id, n.Revision, true); w.Purge(n.Id, n.Revision); Check(!w.All(true).Any(x => x.Id == n.Id)); var json = File.ReadAllText(Path.Combine(w.Path, "records", n.Id + ".json")); Check(json.Contains("PURGED") && !json.Contains("title")); });
    Test("FR05 分页与失效代次", () =>
    {
        for (int i = 0; i < 60; i++) w.Create("分页" + i, "分页测试");
        var page = w.Search("分页", []); Check(page.Items.Count == 50 && page.HasMore); Check(w.Search("分页", [], offset: 50, expectedGeneration: page.Generation).Items.Count == 10);
        w.Create("新的分页"); Throws(() => w.Search("分页", [], offset: 50, expectedGeneration: page.Generation));
    });
}
Test("FR11 重开与设置持久化", () => { using var w = new Workspace(Path.Combine(root, "workspace")); Check(w.Settings.Theme == "dark" && w.IndexReady); });
Test("FR11 损坏 SQLite 自动重建", () => { File.WriteAllText(Path.Combine(root, "workspace", ".index", "index.db"), "corrupt"); using var w = new Workspace(Path.Combine(root, "workspace")); Check(w.IndexReady); Check(w.Search("分页", []).Items.Count == 50); });
foreach (var point in new[] { "dirty", "body", "record" })
{
    Test("NFR04 进程异常退出 / " + point, () =>
    {
        var folder = Folder("crash-" + point); string id;
        using (var w = new Workspace(folder, true)) id = w.Create("提交保护", "最后确认的正文").Id;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(Workspace).Assembly.Location.Replace("Zhijian.Core.dll", "Zhijian.Checks.dll")); start.ArgumentList.Add("--crash"); start.ArgumentList.Add(folder); start.ArgumentList.Add(point);
        using var process = Process.Start(start)!; Check(process.WaitForExit(30000)); Check(process.ExitCode == 73);
        using var reopened = new Workspace(folder); var body = reopened.Get(id).Body;
        Check(body == (point == "record" ? "新提交的内容" : "最后确认的正文")); Check(reopened.IndexReady); Check(reopened.Search(body, []).Items.Single().Id == id);
    });
}
Test("EX10 元数据损坏隔离", () =>
{
    var path = Folder("bad-record"); string id; using (var w = new Workspace(path, true)) id = w.Create("保护", "原正文").Id;
    File.WriteAllText(Path.Combine(path, "records", id + ".json"), "{broken"); using var reopened = new Workspace(path);
    Check(reopened.Warnings.Count > 0 && !reopened.IndexReady); Check(Directory.GetFiles(Path.Combine(path, "bodies"), "*.md", SearchOption.AllDirectories).Length > 0);
});
Test("EX06 未完成恢复不可打开", () => { var path = Folder("incomplete"); File.WriteAllText(Path.Combine(path, "restore-incomplete"), ""); Throws(() => { using var w = new Workspace(path); }); });
Test("FR08 格式化链接与引用式链接迁移", () =>
{
    var id = Guid.NewGuid().ToString();
    var source = $"[**粗体** 标题](note:{id} \"提示\")\n\n[引用][ref]\n\n[ref]: note:{id}\n";
    var changed = MarkdownLinks.Rewrite(source, url => url == "note:" + id ? "./new.md" : null);
    Check(changed.Contains("[**粗体** 标题](./new.md \"提示\")"));
    Check(MarkdownLinks.Links(changed).Count(l => l.Url == "./new.md") == 2, changed);
});
Test("NFR04 写入被锁时保留最后成功版本", () =>
{
    var path = Folder("locked-save"); using var w = new Workspace(path, true); var note = w.Create("旧标题", "旧正文");
    using (var recordLock = new FileStream(Path.Combine(path, "records", note.Id + ".json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        Throws(() => w.Save(note.Id, note.Revision, "新标题", "新正文", []));
    Check(w.Get(note.Id).Body == "旧正文");
    var saved = w.Save(note.Id, note.Revision, "重试成功", "新正文", []); Check(w.Get(saved.Id).Body == "新正文");
});
Test("NFR04 旧正文版本有界清理", () =>
{
    var path = Folder("versions"); using var w = new Workspace(path, true); var n = w.Create("连续保存", "版本0");
    for (int i = 1; i <= 12; i++) n = w.Save(n.Id, n.Revision, n.Title!, "版本" + i, []);
    Check(w.Get(n.Id).Body == "版本12"); Check(Directory.GetFiles(Path.Combine(path, "bodies", n.Id), "*.md").Length == 2);
});
Test("FR09 备份取消不发布完整目录", () =>
{
    using var w = new Workspace(Folder("cancel-backup-ws"), true); w.Create("测试", "正文"); var target = Folder("cancel-backup-target");
    Throws(() => w.Backup(target, cancel: new CancellationToken(true)));
    Check(Directory.EnumerateDirectories(target).All(p => p.EndsWith(".partial")));
});
Test("EX10 正文校验失败阻止编辑", () =>
{
    var path = Folder("bad-body"); using var w = new Workspace(path, true); var n = w.Create("正文保护", "原正文");
    File.WriteAllText(Path.Combine(path, "bodies", n.Id, n.BodyRev + ".md"), "篡改"); Throws(() => w.Get(n.Id));
});
Test("FR08 导入预检缺失目标与同批链接", () =>
{
    var dir = Folder("preview-links"); var a = Path.Combine(dir, "导入甲.md"); var b = Path.Combine(dir, "导入乙.md");
    File.WriteAllText(a, "[去乙](导入乙.md)\n`[忽略](不存在.md)`"); File.WriteAllText(b, "目标");
    var single = ImportPreview.Analyze([a]); Check(single[0].Warnings.Length == 1 && single[0].MappedLinks == 0);
    var batch = ImportPreview.Analyze([a, b]); Check(batch[0].Warnings.Length == 0 && batch[0].MappedLinks == 1);
    using var ws = new Workspace(Folder("preview-import"), true); var report = ws.Import([a, b]);
    Check(report.SuccessCount == 2 && ws.Backlinks(report.Items[1].NoteId!).Length == 1);
});
Test("FR08 导入预检编码大小与无效目标", () =>
{
    var dir = Folder("preview-invalid"); var a = Path.Combine(dir, "a.md"); var b = Path.Combine(dir, "b.md"); var c = Path.Combine(dir, "c.md");
    File.WriteAllText(a, "[坏目标](b.md)"); File.WriteAllBytes(b, [0xc3, 0x28]); File.WriteAllText(c, new string('a', Rules.MaxBodyBytes + 1));
    var preview = ImportPreview.Analyze([a, b, c]); Check(preview.Count(x => !x.CanImport) == 2 && preview[0].Warnings.Length == 1);
    using var ws = new Workspace(Folder("mixed-import"), true); var report = ws.Import([a, b, c]); Check(report.SuccessCount == 1 && report.FailedCount == 2);
    Throws(() => ImportPreview.Analyze(Enumerable.Repeat(a, 501).ToArray()));
    Throws(() => ImportPreview.Analyze([a], new CancellationToken(true)));
});
Test("FR08 导入预检UUID映射且不修改源文件", () =>
{
    var dir = Folder("preview-uuid"); var id = Guid.NewGuid().ToString(); var a = Path.Combine(dir, "a.md"); var b = Path.Combine(dir, id + ".md");
    var body = $"[链接](note:{id})"; File.WriteAllText(a, body); File.WriteAllText(b, "目标");
    Check(ImportPreview.Analyze([a, b])[0].MappedLinks == 1); Check(File.ReadAllText(a) == body);
});
File.WriteAllLines(Path.Combine(root, "checks.txt"), results); Console.WriteLine($"\n{results.Count - failed}/{results.Count} passed. Output: {root}");
Environment.ExitCode = failed == 0 ? 0 : 1;
