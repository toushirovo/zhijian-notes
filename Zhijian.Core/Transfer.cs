using System.Text;

namespace Zhijian.Core;

public sealed partial class Workspace
{
    public BatchReport Import(string[] paths, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        lock (gate)
        {
            Ensure(); if (paths.Length > 500) throw new InvalidDataException("每次最多导入 500 个文件。");
            var report = new BatchReport("导入", []);
            var plans = new List<(string Path, string Title, string Body, string Id, string Hash)>();
            var oldIds = paths.Select(p => System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant()).Where(Rules.IsId).GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
            foreach (var path in paths)
            {
                if (cancel.IsCancellationRequested) { report.Items.Add(new(path, "SKIPPED", Message: "已取消")); continue; }
                try
                {
                    if (!string.Equals(System.IO.Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只支持 .md 文件。");
                    if (new FileInfo(path).Length > Rules.MaxBodyBytes) throw new InvalidDataException("超过 1 MiB。");
                    var bytes = File.ReadAllBytes(path); var body = Rules.Utf8.GetString(bytes).TrimStart('\uFEFF'); Rules.Body(body);
                    var title = Rules.Title(System.IO.Path.GetFileNameWithoutExtension(path));
                    if (oldIds.Contains(title.ToLowerInvariant())) throw new InvalidDataException("同批次旧 ID 重复，无法确定引用目标。");
                    plans.Add((path, title, body, Guid.NewGuid().ToString(), Disk.Hash(bytes)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { report.Items.Add(new(path, "FAILED", Message: ex.Message)); }
            }
            var byOldId = plans.Where(p => Rules.IsId(p.Title.ToLowerInvariant())).ToDictionary(p => p.Title.ToLowerInvariant(), p => p.Id);
            var byPath = plans.ToDictionary(p => System.IO.Path.GetFullPath(p.Path), p => p.Id, StringComparer.OrdinalIgnoreCase);
            int completed = 0;
            foreach (var p in plans)
            {
                if (cancel.IsCancellationRequested) { report.Items.Add(new(p.Path, "SKIPPED", p.Id, "已取消")); continue; }
                try
                {
                    if (Disk.Hash(File.ReadAllBytes(p.Path)) != p.Hash) throw new IOException("文件在检查后发生变化，请重新导入。");
                    var warnings = new List<string>();
                    var body = MarkdownLinks.Rewrite(p.Body, url =>
                    {
                        var old = MarkdownLinks.Target(url);
                        if (old != null)
                        {
                            if (byOldId.TryGetValue(old, out var mapped)) return "note:" + mapped;
                            if (records.ContainsKey(old)) { warnings.Add("未匹配的旧 ID 与已有笔记冲突，已转为普通文本"); return ""; }
                            warnings.Add("存在未导入的 note 链接目标"); return null;
                        }
                        if (Uri.TryCreate(url, UriKind.Absolute, out _)) return null;
                        try
                        {
                            var resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p.Path)!, Uri.UnescapeDataString(url)));
                            if (byPath.TryGetValue(resolved, out var target)) return "note:" + target;
                        }
                        catch (ArgumentException) { }
                        warnings.Add("未匹配的相对链接已转为普通文本"); return "";
                    });
                    Create(p.Title, body, id: p.Id);
                    report.Items.Add(new(p.Path, "SUCCESS", p.Id, string.Join("；", warnings.Distinct())));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { report.Items.Add(new(p.Path, "FAILED", p.Id, ex.Message)); }
                progress?.Report($"正在导入：{++completed}/{plans.Count}");
            }
            var failedIds = report.Items.Where(i => i.Status != "SUCCESS").Select(i => i.NoteId).OfType<string>().ToHashSet();
            for (int i = 0; i < report.Items.Count; i++)
            {
                var item = report.Items[i];
                if (item.Status == "SUCCESS" && MarkdownLinks.Targets(ReadBody(records[item.NoteId!])).Any(failedIds.Contains))
                    report.Items[i] = item with { Message = item.Message + "；引用的批次目标未成功导入，链接暂时失效" };
            }
            return report;
        }
    }
    private string NewOutput(string parent, string prefix)
    {
        parent = Disk.Root(parent);
        if (Disk.Inside(parent, Path)) throw new IOException("输出目录不能位于当前工作区内，请选择其他位置。");
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("输出目录不存在。");
        return System.IO.Path.Combine(parent, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
    }
    public BatchReport Export(string parent, string[]? ids = null, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        lock (gate)
        {
            Ensure(); var notes = ids == null ? All() : ids.Distinct().Select(id => records[id]).ToArray();
            if (notes.Any(n => n.State != "ACTIVE")) throw new InvalidOperationException("只能导出有效笔记。");
            var output = NewOutput(parent, "export"); Directory.CreateDirectory(output);
            var report = new BatchReport("导出", [], output); var exported = notes.Select(n => n.Id).ToHashSet(); int count = 0;
            foreach (var n in notes)
            {
                if (cancel.IsCancellationRequested) { report.Items.Add(new(n.Title!, "SKIPPED", n.Id, "已取消")); continue; }
                try
                {
                    var outside = false;
                    var body = MarkdownLinks.Rewrite(ReadBody(n), url =>
                    {
                        var target = MarkdownLinks.Target(url); if (target == null) return null;
                        if (exported.Contains(target)) return "./" + target + ".md";
                        outside = true; return null;
                    });
                    Disk.Atomic(System.IO.Path.Combine(output, n.Id + ".md"), Rules.Utf8.GetBytes(body));
                    report.Items.Add(new(n.Title!, "SUCCESS", n.Id, outside ? "含本次导出范围之外的 note 链接" : null));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                { report.Items.Add(new(n.Title!, "FAILED", n.Id, ex.Message)); }
                progress?.Report($"正在导出：{++count}/{notes.Length}");
            }
            Disk.Atomic(System.IO.Path.Combine(output, "report.json"), Disk.Serialize(report)); return report;
        }
    }
    public string Backup(string parent, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        lock (gate)
        {
            Ensure(); if (Warnings.Any(w => w.StartsWith("已隔离"))) throw new InvalidDataException("存在损坏记录，不能创建会遗漏笔记的备份。请先处理或复制整个工作区留存。");
            var output = NewOutput(parent, "backup"); var partial = output + ".partial"; Directory.CreateDirectory(partial);
            var notes = records.Values.Where(n => n.State != "PURGED").ToArray();
            var paths = new List<string> { "workspace.json", "settings.json" };
            foreach (var n in notes) { ReadBody(n); paths.Add($"records/{n.Id}.json"); paths.Add($"bodies/{n.Id}/{n.BodyRev}.md"); }
            var files = new List<BackupFile>(); int count = 0;
            foreach (var relative in paths)
            {
                cancel.ThrowIfCancellationRequested(); var source = Disk.SafeFile(Path, relative); var dest = Disk.SafeFile(partial, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!); var bytes = File.ReadAllBytes(source); Disk.Durable(dest, bytes);
                files.Add(new(relative, bytes.LongLength, Disk.Hash(bytes))); progress?.Report($"正在备份：{++count}/{paths.Count}");
            }
            var manifest = new BackupManifest(1, Info.WorkspaceId, DateTimeOffset.UtcNow, notes.Count(n => n.State == "ACTIVE"), notes.Count(n => n.State == "TRASH"), files);
            Disk.Durable(System.IO.Path.Combine(partial, "manifest.json"), Disk.Serialize(manifest));
            ValidateBackup(partial, cancel); Directory.Move(partial, output); return output;
        }
    }
    public static BackupManifest ValidateBackup(string folder, CancellationToken cancel = default)
    {
        folder = Disk.Root(folder); var manifestPath = Disk.SafeFile(folder, "manifest.json");
        if (new FileInfo(manifestPath).Length > 16 * 1024 * 1024) throw new InvalidDataException("备份清单过大。");
        var manifest = Disk.Read<BackupManifest>(manifestPath);
        if (manifest.FormatVersion != 1 || !Rules.IsId(manifest.SourceWorkspaceId) || manifest.Files == null || manifest.Files.Count > 100002)
            throw new InvalidDataException("备份版本或清单不合法。");
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested(); var path = Disk.SafeFile(folder, f.Path);
            if (f.Path == "manifest.json" || !listed.Add(f.Path) || f.Bytes < 0 || f.Bytes > 2 * 1024 * 1024 ||
                new FileInfo(path).Length != f.Bytes || Disk.Hash(File.ReadAllBytes(path)) != f.Sha256) throw new InvalidDataException($"备份校验失败：{f.Path}。原工作区未改动。");
        }
        // Enumerate without following any reparse points, including unused directories.
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Walk(string directory)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                Disk.NoReparse(item);
                if (Directory.Exists(item)) Walk(item);
                else actual.Add(System.IO.Path.GetRelativePath(folder, item).Replace('\\', '/'));
            }
        }
        Walk(folder); actual.Remove("manifest.json");
        if (!actual.SetEquals(listed)) throw new InvalidDataException("备份含额外文件或缺少文件。");
        if (!listed.Contains("workspace.json") || !listed.Contains("settings.json")) throw new InvalidDataException("备份缺少工作区或设置。");
        var info = Disk.Read<WorkspaceInfo>(Disk.SafeFile(folder, "workspace.json"));
        var settings = Disk.Read<Preferences>(Disk.SafeFile(folder, "settings.json"));
        if (info.SchemaVersion != 1 || info.WorkspaceId != manifest.SourceWorkspaceId || settings.SchemaVersion != 1 || settings.Theme is not ("light" or "dark")) throw new InvalidDataException("工作区或设置版本不兼容。");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "workspace.json", "settings.json" }; int active = 0, trash = 0;
        foreach (var path in listed.Where(p => p.StartsWith("records/", StringComparison.Ordinal)))
        {
            var n = Disk.Read<NoteRecord>(Disk.SafeFile(folder, path)); Rules.Validate(n);
            if (n.State == "PURGED" || path != $"records/{n.Id}.json") throw new InvalidDataException("备份记录路径或状态不合法。");
            expected.Add(path); var body = $"bodies/{n.Id}/{n.BodyRev}.md"; expected.Add(body);
            if (!listed.Contains(body)) throw new InvalidDataException("备份缺少引用的正文。");
            var bytes = File.ReadAllBytes(Disk.SafeFile(folder, body)); Rules.Utf8.GetString(bytes);
            if (bytes.Length != n.BodyBytes || Disk.Hash(bytes) != n.BodySha256) throw new InvalidDataException("正文与元数据校验不一致。");
            if (n.State == "ACTIVE") active++; else trash++;
        }
        if (!expected.SetEquals(listed) || active != manifest.NoteCount || trash != manifest.TrashCount) throw new InvalidDataException("备份笔记数量或引用清单不一致。");
        return manifest;
    }
    public static void RestoreBackup(string source, string target, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        source = Disk.Root(source); target = Disk.Root(target);
        if (Disk.Inside(target, source) || Disk.Inside(source, target)) throw new IOException("恢复目录与备份目录不能互相包含。");
        var manifest = ValidateBackup(source, cancel);
        if (!Directory.Exists(target) || Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("恢复目标必须是已有的空目录。");
        var marker = System.IO.Path.Combine(target, "restore-incomplete");
        using (var targetLock = new FileStream(marker, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            int count = 0;
            foreach (var f in manifest.Files)
            {
                cancel.ThrowIfCancellationRequested(); var bytes = File.ReadAllBytes(Disk.SafeFile(source, f.Path));
                if (bytes.LongLength != f.Bytes || Disk.Hash(bytes) != f.Sha256) throw new InvalidDataException("备份在恢复期间发生变化，请重新校验。");
                var dest = Disk.SafeFile(target, f.Path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!); Disk.Durable(dest, bytes);
                if (Disk.Hash(File.ReadAllBytes(dest)) != f.Sha256) throw new IOException("恢复文件校验失败。");
                progress?.Report($"正在恢复：{++count}/{manifest.Files.Count}");
            }
        }
        File.Delete(marker);
    }
}
